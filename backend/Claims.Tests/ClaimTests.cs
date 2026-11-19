using System.Reflection;
using Claims.Api.Domain;

namespace Claims.Tests;

public class ClaimTests
{
    static readonly Guid Claimant = Guid.NewGuid();
    static readonly Guid Officer = Guid.NewGuid();
    static readonly Guid OtherOfficer = Guid.NewGuid();
    static readonly DateTimeOffset T0 = new(2026, 1, 1, 9, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset T1 = T0.AddHours(1);

    public enum Act { Assign, RequestInfo, ProvideInfo, SetReserve, Settle, Reject }

    static Claim NewClaim() =>
        Claim.Submit(Claimant, ClaimType.Motor, Market.AU, "POL-1", "Rear-ended at lights", T0);

    static Claim ClaimIn(ClaimStatus status)
    {
        var c = NewClaim();
        if (status != ClaimStatus.Submitted)
        {
            c.Assign(Officer, T0);
            c.SetReserve(Officer, 1000m, T0);
        }
        if (status == ClaimStatus.AwaitingInfo) c.RequestInfo(Officer, "Photos?", T0);
        if (status == ClaimStatus.Settled) c.Settle(Officer, 500m, T0);
        if (status == ClaimStatus.Rejected) c.Reject(Officer, "Not covered", T0);
        c.ClearDomainEvents();
        return c;
    }

    static void Do(Claim c, Act act, Guid? actor = null)
    {
        switch (act)
        {
            case Act.Assign: c.Assign(actor ?? Officer, T1); break;
            case Act.RequestInfo: c.RequestInfo(actor ?? Officer, "Photos?", T1); break;
            case Act.ProvideInfo: c.ProvideInfo(actor ?? Claimant, "Attached", T1); break;
            case Act.SetReserve: c.SetReserve(actor ?? Officer, 1200m, T1); break;
            case Act.Settle: c.Settle(actor ?? Officer, 800m, T1); break;
            case Act.Reject: c.Reject(actor ?? Officer, "Not covered", T1); break;
        }
    }

    static void AssertRefused(Claim c, Action action)
    {
        var (status, history, version, reserve) = (c.Status, c.History.Count, c.Version, c.Reserve);
        Assert.Throws<DomainException>(action);
        Assert.Equal(status, c.Status);
        Assert.Equal(history, c.History.Count);
        Assert.Equal(version, c.Version);
        Assert.Equal(reserve, c.Reserve);
        Assert.Empty(c.DomainEvents);
    }

    // Every (status, action) pair. null = refused.
    [Theory]
    [InlineData(ClaimStatus.Submitted, Act.Assign, ClaimStatus.InReview)]
    [InlineData(ClaimStatus.Submitted, Act.RequestInfo, null)]
    [InlineData(ClaimStatus.Submitted, Act.ProvideInfo, null)]
    [InlineData(ClaimStatus.Submitted, Act.SetReserve, null)]
    [InlineData(ClaimStatus.Submitted, Act.Settle, null)]
    [InlineData(ClaimStatus.Submitted, Act.Reject, null)]
    [InlineData(ClaimStatus.InReview, Act.Assign, null)]
    [InlineData(ClaimStatus.InReview, Act.RequestInfo, ClaimStatus.AwaitingInfo)]
    [InlineData(ClaimStatus.InReview, Act.ProvideInfo, null)]
    [InlineData(ClaimStatus.InReview, Act.SetReserve, ClaimStatus.InReview)]
    [InlineData(ClaimStatus.InReview, Act.Settle, ClaimStatus.Settled)]
    [InlineData(ClaimStatus.InReview, Act.Reject, ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.AwaitingInfo, Act.Assign, null)]
    [InlineData(ClaimStatus.AwaitingInfo, Act.RequestInfo, null)]
    [InlineData(ClaimStatus.AwaitingInfo, Act.ProvideInfo, ClaimStatus.InReview)]
    [InlineData(ClaimStatus.AwaitingInfo, Act.SetReserve, ClaimStatus.AwaitingInfo)]
    [InlineData(ClaimStatus.AwaitingInfo, Act.Settle, null)]
    [InlineData(ClaimStatus.AwaitingInfo, Act.Reject, ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.Settled, Act.Assign, null)]
    [InlineData(ClaimStatus.Settled, Act.RequestInfo, null)]
    [InlineData(ClaimStatus.Settled, Act.ProvideInfo, null)]
    [InlineData(ClaimStatus.Settled, Act.SetReserve, null)]
    [InlineData(ClaimStatus.Settled, Act.Settle, null)]
    [InlineData(ClaimStatus.Settled, Act.Reject, null)]
    [InlineData(ClaimStatus.Rejected, Act.Assign, null)]
    [InlineData(ClaimStatus.Rejected, Act.RequestInfo, null)]
    [InlineData(ClaimStatus.Rejected, Act.ProvideInfo, null)]
    [InlineData(ClaimStatus.Rejected, Act.SetReserve, null)]
    [InlineData(ClaimStatus.Rejected, Act.Settle, null)]
    [InlineData(ClaimStatus.Rejected, Act.Reject, null)]
    public void Status_flow(ClaimStatus from, Act act, ClaimStatus? to)
    {
        var c = ClaimIn(from);

        if (to is null)
        {
            AssertRefused(c, () => Do(c, act));
            return;
        }

        var (history, version) = (c.History.Count, c.Version);
        Do(c, act);
        Assert.Equal(to, c.Status);
        Assert.Equal(history + 1, c.History.Count);
        Assert.Equal(version + 1, c.Version);
        Assert.Single(c.DomainEvents);
    }

    [Theory]
    [InlineData(ClaimStatus.InReview, Act.RequestInfo)]
    [InlineData(ClaimStatus.InReview, Act.SetReserve)]
    [InlineData(ClaimStatus.InReview, Act.Settle)]
    [InlineData(ClaimStatus.InReview, Act.Reject)]
    [InlineData(ClaimStatus.AwaitingInfo, Act.SetReserve)]
    [InlineData(ClaimStatus.AwaitingInfo, Act.Reject)]
    public void Only_assigned_officer_can_act(ClaimStatus from, Act act)
    {
        var c = ClaimIn(from);
        AssertRefused(c, () => Do(c, act, OtherOfficer));
        AssertRefused(c, () => Do(c, act, Claimant));
    }

    [Fact]
    public void Only_owner_can_provide_info()
    {
        var c = ClaimIn(ClaimStatus.AwaitingInfo);
        AssertRefused(c, () => Do(c, Act.ProvideInfo, Officer));
        AssertRefused(c, () => Do(c, Act.ProvideInfo, Guid.NewGuid()));
    }

    [Fact]
    public void Claimant_cannot_assign_own_claim()
    {
        var c = ClaimIn(ClaimStatus.Submitted);
        AssertRefused(c, () => Do(c, Act.Assign, Claimant));
    }

    [Fact]
    public void Reserve_must_not_be_negative()
    {
        var c = ClaimIn(ClaimStatus.InReview);
        AssertRefused(c, () => c.SetReserve(Officer, -0.01m, T1));

        c.SetReserve(Officer, 0m, T1);
        Assert.Equal(0m, c.Reserve);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Settle_amount_must_be_positive(int amount)
    {
        var c = ClaimIn(ClaimStatus.InReview);
        AssertRefused(c, () => c.Settle(Officer, amount, T1));
    }

    [Fact]
    public void Settle_cannot_exceed_reserve()
    {
        var c = ClaimIn(ClaimStatus.InReview);
        c.SetReserve(Officer, 5000m, T1);
        c.ClearDomainEvents();

        AssertRefused(c, () => c.Settle(Officer, 6200m, T1));

        c.Settle(Officer, 5000m, T1); // equal to reserve is fine
        Assert.Equal(ClaimStatus.Settled, c.Status);
    }

    // Money is stored as numeric(18,2); more decimals would be rounded silently.
    [Fact]
    public void Amounts_have_at_most_two_decimals()
    {
        var c = ClaimIn(ClaimStatus.InReview);
        AssertRefused(c, () => c.SetReserve(Officer, 1000.005m, T1));
        AssertRefused(c, () => c.Settle(Officer, 500.001m, T1));

        c.SetReserve(Officer, 1000.500m, T1); // trailing zero is still 2 decimals
        c.Settle(Officer, 999.99m, T1);
        Assert.Equal(999.99m, c.SettledAmount);
    }

    [Theory]
    [InlineData(Act.RequestInfo, "")]
    [InlineData(Act.RequestInfo, "   ")]
    [InlineData(Act.ProvideInfo, "")]
    [InlineData(Act.Reject, " ")]
    public void Question_answer_and_reason_are_required(Act act, string text)
    {
        var c = ClaimIn(act == Act.ProvideInfo ? ClaimStatus.AwaitingInfo : ClaimStatus.InReview);
        Action action = act switch
        {
            Act.RequestInfo => () => c.RequestInfo(Officer, text, T1),
            Act.ProvideInfo => () => c.ProvideInfo(Claimant, text, T1),
            _ => () => c.Reject(Officer, text, T1),
        };
        AssertRefused(c, action);
    }

    [Fact]
    public void Submit_creates_claim_with_market_currency_history_and_event()
    {
        var c = NewClaim();

        Assert.NotEqual(Guid.Empty, c.Id);
        Assert.Equal(ClaimStatus.Submitted, c.Status);
        Assert.Equal(Claimant, c.ClaimantId);
        Assert.Null(c.AssignedOfficerId);
        Assert.Equal("AUD", c.Currency);
        Assert.Equal(0m, c.Reserve);
        Assert.Null(c.SettledAmount);
        Assert.Equal(T0, c.SubmittedAt);

        var h = Assert.Single(c.History);
        Assert.Equal((ClaimEventType.Submitted, "Submitted", Claimant, T0), (h.EventType, h.NewValue, h.ActorId, h.At));

        var e = Assert.Single(c.DomainEvents);
        Assert.Equal((ClaimEventType.Submitted, c.Id, Claimant), (e.Type, e.ClaimId, e.ActorId));
    }

    // Each action: one history row and one domain event, both with the right type, actor and time.
    [Theory]
    [InlineData(ClaimStatus.Submitted, Act.Assign, ClaimEventType.Assigned, "Submitted", "InReview", null)]
    [InlineData(ClaimStatus.InReview, Act.RequestInfo, ClaimEventType.InfoRequested, "InReview", "AwaitingInfo", "Photos?")]
    [InlineData(ClaimStatus.AwaitingInfo, Act.ProvideInfo, ClaimEventType.InfoProvided, "AwaitingInfo", "InReview", "Attached")]
    [InlineData(ClaimStatus.InReview, Act.SetReserve, ClaimEventType.ReserveSet, "1000", "1200", null)]
    [InlineData(ClaimStatus.InReview, Act.Settle, ClaimEventType.Settled, "InReview", "Settled", "800 AUD")]
    [InlineData(ClaimStatus.InReview, Act.Reject, ClaimEventType.Rejected, "InReview", "Rejected", "Not covered")]
    [InlineData(ClaimStatus.AwaitingInfo, Act.Reject, ClaimEventType.Rejected, "AwaitingInfo", "Rejected", "Not covered")]
    public void Action_adds_history_and_domain_event(
        ClaimStatus from, Act act, ClaimEventType type, string oldValue, string newValue, string? note)
    {
        var c = ClaimIn(from);
        var oldStatus = c.Status;
        var actor = act == Act.ProvideInfo ? Claimant : Officer;

        Do(c, act);

        var h = c.History.Last();
        Assert.Equal(type, h.EventType);
        Assert.Equal(c.Id, h.ClaimId);
        Assert.Equal(oldValue, h.OldValue);
        Assert.Equal(newValue, h.NewValue);
        Assert.Equal(note, h.Note);
        Assert.Equal(actor, h.ActorId);
        Assert.Equal(T1, h.At);

        var e = Assert.Single(c.DomainEvents);
        Assert.Equal(type, e.Type);
        Assert.Equal(c.Id, e.ClaimId);
        Assert.Equal(actor, e.ActorId);
        Assert.Equal(oldStatus, e.OldStatus);
        Assert.Equal(c.Status, e.NewStatus);
        Assert.Equal(c.Market, e.Market);
        Assert.Equal(c.Type, e.ClaimType);
        Assert.Equal("AUD", e.Currency);
        Assert.Equal(c.Reserve, e.Reserve);
        Assert.Equal(T1, e.At);
    }

    [Fact]
    public void Assign_sets_officer()
    {
        var c = ClaimIn(ClaimStatus.Submitted);
        c.Assign(Officer, T1);
        Assert.Equal(Officer, c.AssignedOfficerId);
    }

    [Fact]
    public void Set_reserve_event_carries_old_and_new_reserve()
    {
        var c = ClaimIn(ClaimStatus.InReview);
        c.SetReserve(Officer, 1000m, T1);
        c.ClearDomainEvents();

        c.SetReserve(Officer, 1500.50m, T1);

        Assert.Equal(1500.50m, c.Reserve);
        Assert.Equal(("1000", "1500.50"), (c.History.Last().OldValue, c.History.Last().NewValue));
        var e = Assert.Single(c.DomainEvents);
        Assert.Equal((1000m, 1500.50m), (e.OldReserve, e.Reserve));
    }

    [Fact]
    public void Settle_records_amount_and_close_time()
    {
        var c = ClaimIn(ClaimStatus.InReview);
        c.Settle(Officer, 800m, T1);

        Assert.Equal(800m, c.SettledAmount);
        Assert.Equal(T1, c.ClosedAt);
        Assert.Equal(800m, Assert.Single(c.DomainEvents).SettledAmount);
    }

    [Fact]
    public void Reject_records_close_time()
    {
        var c = ClaimIn(ClaimStatus.InReview);
        c.Reject(Officer, "Not covered", T1);

        Assert.Equal(T1, c.ClosedAt);
        Assert.Null(c.SettledAmount);
    }

    [Fact]
    public void Clear_domain_events_empties_the_list()
    {
        var c = NewClaim();
        c.ClearDomainEvents();
        Assert.Empty(c.DomainEvents);
    }

    // Target is in days since submit; the clock does not pause during AwaitingInfo (assumption 8).
    [Theory]
    [InlineData(ClaimStatus.Submitted, 13, false)]
    [InlineData(ClaimStatus.Submitted, 14, false)]
    [InlineData(ClaimStatus.Submitted, 15, true)]
    [InlineData(ClaimStatus.InReview, 20, true)]
    [InlineData(ClaimStatus.AwaitingInfo, 20, true)]
    [InlineData(ClaimStatus.Settled, 20, false)]
    [InlineData(ClaimStatus.Rejected, 20, false)]
    public void Overdue_rule(ClaimStatus status, int daysSinceSubmit, bool overdue)
    {
        var c = ClaimIn(status);
        Assert.Equal(overdue, c.IsOverdue(T0.AddDays(daysSinceSubmit), targetDays: 14));
    }

    [Theory]
    [InlineData(Market.AU, "AUD")]
    [InlineData(Market.ID, "IDR")]
    [InlineData(Market.SG, "SGD")]
    [InlineData(Market.HK, "HKD")]
    [InlineData(Market.MY, "MYR")]
    [InlineData(Market.TH, "THB")]
    public void Market_sets_currency(Market market, string currency)
    {
        Assert.Equal(currency, market.Currency());
        Assert.Equal(currency, Claim.Submit(Claimant, ClaimType.Property, market, "P", "D", T0).Currency);
    }

    // Guards "ways around the rules": state only changes through Claim methods.
    [Theory]
    [InlineData(typeof(Claim))]
    [InlineData(typeof(ClaimHistory))]
    public void Domain_types_have_no_public_setters(Type type)
    {
        var settable = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod?.IsPublic == true)
            .Select(p => p.Name);
        Assert.Empty(settable);
    }

    [Fact]
    public void History_and_events_cannot_be_modified_from_outside()
    {
        var c = NewClaim();
        Assert.False(c.History is List<ClaimHistory>);
        Assert.False(c.DomainEvents is List<ClaimEvent>);
    }
}
