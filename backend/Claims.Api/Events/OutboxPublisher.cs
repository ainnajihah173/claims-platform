using System.Text.Json;
using System.Text.Json.Serialization;
using Claims.Api.Data;
using Claims.Api.Domain;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Events;

// The message on claims.events. Ids, statuses and amounts only: no names, notes, policy number or description.
public record ClaimEventMessage(
    Guid EventId,
    ClaimEventType EventType,
    int SchemaVersion,
    DateTimeOffset OccurredAt,
    Guid ClaimId,
    Guid ActorId,
    ClaimStatus? OldStatus,
    ClaimStatus NewStatus,
    Market Market,
    ClaimType ClaimType,
    string Currency,
    decimal OldReserve,
    decimal Reserve,
    decimal? SettledAmount)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static ClaimEventMessage From(Guid eventId, ClaimEvent e) => new(
        eventId, e.Type, 1, e.At, e.ClaimId, e.ActorId, e.OldStatus, e.NewStatus, e.Market, e.ClaimType, e.Currency,
        e.OldReserve, e.Reserve, e.SettledAmount);
}

// Every second: send unpublished outbox rows to Kafka in order, wait for the ack, then mark them published.
public class OutboxPublisher(IServiceScopeFactory scopes, IConfiguration config, ILogger<OutboxPublisher> log) : BackgroundService
{
    public const string Topic = "claims.events";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"],
            EnableIdempotence = true, // the broker drops a retried send it already wrote
            Acks = Acks.All,
        }).Build();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { await PublishPending(producer, ct); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                log.LogWarning(e, "Outbox publish failed; trying again in 1 s");
            }
        }
    }

    async Task PublishPending(IProducer<string, string> producer, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // shortcut: one publisher per database; with several API instances, claim rows with FOR UPDATE SKIP LOCKED.
        var rows = await db.Outbox.Where(o => o.PublishedAt == null)
            .OrderBy(o => o.OccurredAt).ThenBy(o => o.Id).Take(100).ToListAsync(ct);
        foreach (var row in rows)
        {
            var claimId = JsonSerializer.Deserialize<ClaimEventMessage>(row.Payload, ClaimEventMessage.Json)!.ClaimId;
            // Key = claimId: one claim's events go to one partition, so they stay in order.
            // A crash after the ack but before the save sends the row again; the consumer skips the duplicate.
            await producer.ProduceAsync(Topic, new Message<string, string> { Key = claimId.ToString(), Value = row.Payload }, ct);
            row.PublishedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }
}
