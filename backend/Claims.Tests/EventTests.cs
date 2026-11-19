using System.Text.Json;
using Claims.Api.Data;
using Claims.Api.Domain;
using Claims.Api.Events;

namespace Claims.Tests;

public class EventTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    static readonly Guid Officer = Guid.NewGuid();

    static Claim ReservedClaim()
    {
        var claim = Claim.Submit(Guid.NewGuid(), ClaimType.Motor, Market.AU, "AU-MOT-SECRET", "Hit a tree on Smith St", Now);
        claim.Assign(Officer, Now);
        claim.SetReserve(Officer, 1000m, Now);
        return claim;
    }

    static ExposureSummary Project(Claim claim)
    {
        var row = new ExposureSummary();
        foreach (var e in claim.DomainEvents) ProjectionConsumer.ApplyExposure(row, ClaimEventMessage.From(Guid.NewGuid(), e));
        return row;
    }

    [Fact]
    public void Open_claim_counts_once_with_its_reserve()
    {
        var row = Project(ReservedClaim());
        Assert.Equal((1, 1000m), (row.OpenCount, row.TotalReserve));
    }

    [Fact]
    public void Settle_removes_the_last_reserve_not_the_payout()
    {
        var claim = ReservedClaim();
        claim.Settle(Officer, 800m, Now);
        var row = Project(claim);
        Assert.Equal((0, 0m), (row.OpenCount, row.TotalReserve));
    }

    [Fact]
    public void Reject_removes_the_reserve()
    {
        var claim = ReservedClaim();
        claim.Reject(Officer, "Not covered for Mr Smith", Now);
        var row = Project(claim);
        Assert.Equal((0, 0m), (row.OpenCount, row.TotalReserve));
    }

    [Fact]
    public void Payload_has_no_personal_data()
    {
        var claim = ReservedClaim();
        claim.RequestInfo(Officer, "Send Mr Smith's licence", Now);
        claim.Reject(Officer, "Not covered for Mr Smith", Now);
        foreach (var e in claim.DomainEvents)
        {
            var json = JsonSerializer.Serialize(ClaimEventMessage.From(Guid.NewGuid(), e), ClaimEventMessage.Json);
            Assert.DoesNotContain("SECRET", json);
            Assert.DoesNotContain("Smith", json);
        }
    }
}
