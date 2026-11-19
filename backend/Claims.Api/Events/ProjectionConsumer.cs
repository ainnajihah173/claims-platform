using System.Globalization;
using System.Text.Json;
using Claims.Api.Data;
using Claims.Api.Domain;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Events;

// Reads claims.events and updates exposure_summary and claimant notifications. Each event counts once.
public class ProjectionConsumer(IServiceScopeFactory scopes, IConfiguration config, ILogger<ProjectionConsumer> log) : BackgroundService
{
    // Consume() blocks its thread, so the loop gets its own instead of holding up host startup.
    protected override Task ExecuteAsync(CancellationToken ct) => Task.Run(() => Run(ct), ct);

    async Task Run(CancellationToken ct)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"],
            GroupId = "claims-projection",
            EnableAutoCommit = false, // commit only after the DB commit
            AutoOffsetReset = AutoOffsetReset.Earliest,
            // Subscribing creates the topic if missing. The very first Consume can still report it missing once; the retry below recovers.
            AllowAutoCreateTopics = true,
        }).Build();
        consumer.Subscribe(OutboxPublisher.Topic);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;
                try
                {
                    result = consumer.Consume(ct);
                    await Project(result.Message.Value, ct);
                    // A crash between the DB commit and this one replays the message; processed_events skips it.
                    consumer.Commit(result);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // shortcut: no retry limit/DLQ, a message that always fails (unknown claim, bad payload, exposure total
                    // past numeric(18,2)) blocks its partition; add a retry topic + DLQ before production.
                    log.LogError(e, "Projection failed at {Offset}; trying again in 5 s", result?.TopicPartitionOffset);
                    // Seek throws if the partition was revoked; uncaught here it would stop the whole API (host default).
                    // Not committed either way, so the message is read again.
                    try { if (result is not null) consumer.Seek(result.TopicPartitionOffset); }
                    catch (KafkaException se) { log.LogWarning(se, "Seek back failed; the next owner of the partition reads it again"); }
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }
        }
        catch (OperationCanceledException) { }
        finally { consumer.Close(); }
    }

    async Task Project(string payload, CancellationToken ct)
    {
        var e = JsonSerializer.Deserialize<ClaimEventMessage>(payload, ClaimEventMessage.Json)!;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Kafka delivers at least once. Seen before → skip; two at once → the primary key fails the second save.
        if (await db.ProcessedEvents.AnyAsync(p => p.EventId == e.EventId, ct)) return;
        db.ProcessedEvents.Add(new ProcessedEvent { EventId = e.EventId, ProcessedAt = DateTimeOffset.UtcNow });

        if (e.EventType is ClaimEventType.Submitted or ClaimEventType.ReserveSet or ClaimEventType.Settled or ClaimEventType.Rejected)
        {
            // shortcut: read-modify-write, safe with one consumer instance; use UPDATE ... SET x = x + @delta when scaling out.
            var row = await db.ExposureSummary.FindAsync([e.Market, e.Currency, e.ClaimType], ct);
            if (row is null)
                db.ExposureSummary.Add(row = new ExposureSummary { Market = e.Market, Currency = e.Currency, ClaimType = e.ClaimType });
            ApplyExposure(row, e);
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var message = e.EventType switch
        {
            ClaimEventType.InfoRequested => "Your claim needs more information. Please answer the question on the claim.",
            ClaimEventType.Settled => string.Create(CultureInfo.InvariantCulture, $"Your claim was settled for {e.SettledAmount:0.00} {e.Currency}."),
            ClaimEventType.Rejected => "Your claim was rejected. See the claim for the reason.",
            _ => null,
        };
        if (message is not null)
        {
            var claimantId = await db.Claims.Where(c => c.Id == e.ClaimId).Select(c => c.ClaimantId).SingleAsync(ct);
            db.Notifications.Add(new Notification(Guid.CreateVersion7(), claimantId, e.ClaimId, message, e.OccurredAt));
        }

        await db.SaveChangesAsync(ct); // one transaction: processed_events + exposure + notification
    }

    // Settled / Rejected remove the claim's last reserve, not the payout: reserve 1,000 settled for 800 removes 1,000.
    public static void ApplyExposure(ExposureSummary row, ClaimEventMessage e)
    {
        switch (e.EventType)
        {
            case ClaimEventType.Submitted:
                row.OpenCount++;
                break;
            case ClaimEventType.ReserveSet:
                row.TotalReserve += e.Reserve - e.OldReserve;
                break;
            case ClaimEventType.Settled or ClaimEventType.Rejected:
                row.OpenCount--;
                row.TotalReserve -= e.Reserve;
                break;
        }
    }
}
