using System.Net.Http.Json;
using Claims.Api.Controllers;
using Claims.Api.Data;
using Testcontainers.Kafka;

namespace Claims.Tests;

// Same API host as the other tests, but with a real broker, so the outbox publisher and projection consumer run.
public sealed class KafkaFixture : ApiFixture
{
    readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.1.0").Build();

    protected override async Task<string> KafkaBootstrap()
    {
        await _kafka.StartAsync();
        return _kafka.GetBootstrapAddress();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _kafka.DisposeAsync();
    }
}

public class KafkaTests(KafkaFixture api) : IClassFixture<KafkaFixture>
{
    [Fact]
    public async Task Claim_events_flow_through_kafka_to_exposure_and_notifications()
    {
        var aisha = await api.LoginAs(Seed.Aisha);
        var chen = await api.LoginAs(Seed.Chen);
        // HK has no seeded claims, so its exposure row holds only this claim.
        var res = await aisha.PostAsJsonAsync("/api/claims",
            new { type = "Property", market = "HK", policyNumber = "HK-PRP-9999", description = "Kafka test" });
        var id = (await res.Content.ReadFromJsonAsync<ClaimDetail>(ApiFixture.Json))!.Claim.Id;
        (await chen.PostAsync($"/api/claims/{id}/assign", null)).EnsureSuccessStatusCode();
        (await chen.PutAsJsonAsync($"/api/claims/{id}/reserve", new { amount = 1000m })).EnsureSuccessStatusCode();

        await Until(async () => await HkExposure(chen) == (1, 1000m));

        (await chen.PostAsJsonAsync($"/api/claims/{id}/settle", new { amount = 800m })).EnsureSuccessStatusCode();

        // Settling releases the 1,000 reserve, not the 800 paid, and tells the claimant.
        await Until(async () => await HkExposure(chen) == (0, 0m));
        await Until(async () => (await aisha.GetFromJsonAsync<List<NotificationItem>>("/api/notifications"))!
            .Any(n => n.ClaimId == id && n.Message == "Your claim was settled for 800.00 HKD."));
    }

    static async Task<(int, decimal)?> HkExposure(HttpClient staff)
    {
        var rows = await staff.GetFromJsonAsync<List<ExposureRow>>("/api/dashboard/exposure", ApiFixture.Json);
        var hk = rows!.SingleOrDefault(r => r.Market == Claims.Api.Domain.Market.HK);
        return hk is null ? null : (hk.OpenCount, hk.TotalReserve);
    }

    // The projection is eventually consistent; the first poll can also hit the consumer's 5 s "unknown topic" retry.
    static async Task Until(Func<Task<bool>> condition)
    {
        for (var deadline = DateTime.UtcNow.AddSeconds(60); DateTime.UtcNow < deadline; await Task.Delay(250))
            if (await condition()) return;
        Assert.Fail("Timed out waiting for the projection");
    }
}
