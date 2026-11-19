using System.Globalization;

namespace Claims.Api.Domain;

public class DomainException(string message) : Exception(message);

public class Claim
{
    readonly List<ClaimHistory> _history = [];
    readonly List<ClaimEvent> _events = [];

    public Guid Id { get; private set; }
    public Guid ClaimantId { get; private set; }
    public Guid? AssignedOfficerId { get; private set; }
    public ClaimType Type { get; private set; }
    public Market Market { get; private set; }
    public string Currency { get; private set; } = "";
    public string PolicyNumber { get; private set; } = "";
    public string Description { get; private set; } = "";
    public ClaimStatus Status { get; private set; }
    public decimal Reserve { get; private set; }
    public decimal? SettledAmount { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public int Version { get; private set; }

    public IReadOnlyList<ClaimHistory> History => _history.AsReadOnly();
    public IReadOnlyList<ClaimEvent> DomainEvents => _events.AsReadOnly();

    Claim() { } // EF Core

    public static Claim Submit(Guid claimantId, ClaimType type, Market market, string policyNumber,
        string description, DateTimeOffset now)
    {
        var claim = new Claim
        {
            Id = Guid.CreateVersion7(),
            ClaimantId = claimantId,
            Type = type,
            Market = market,
            Currency = market.Currency(),
            PolicyNumber = policyNumber,
            Description = description,
            Status = ClaimStatus.Submitted,
            SubmittedAt = now,
        };
        claim.Record(ClaimEventType.Submitted, claimantId, now, null, null, nameof(ClaimStatus.Submitted), null, 0m);
        return claim;
    }

    public void Assign(Guid officerId, DateTimeOffset now)
    {
        Require(Status == ClaimStatus.Submitted, "Only a submitted claim can be picked up.");
        Require(officerId != ClaimantId, "A claimant cannot handle their own claim.");
        AssignedOfficerId = officerId;
        ChangeStatus(ClaimEventType.Assigned, officerId, ClaimStatus.InReview, null, now);
    }

    public void RequestInfo(Guid officerId, string question, DateTimeOffset now)
    {
        RequireAssignedOfficer(officerId);
        Require(Status == ClaimStatus.InReview, "Information can only be requested on a claim in review.");
        RequireText(question, "Question");
        ChangeStatus(ClaimEventType.InfoRequested, officerId, ClaimStatus.AwaitingInfo, question, now);
    }

    public void ProvideInfo(Guid claimantId, string answer, DateTimeOffset now)
    {
        Require(claimantId == ClaimantId, "Only the claimant can provide information.");
        Require(Status == ClaimStatus.AwaitingInfo, "No information was requested.");
        RequireText(answer, "Answer");
        ChangeStatus(ClaimEventType.InfoProvided, claimantId, ClaimStatus.InReview, answer, now);
    }

    public void SetReserve(Guid officerId, decimal amount, DateTimeOffset now)
    {
        RequireAssignedOfficer(officerId);
        Require(Status is ClaimStatus.InReview or ClaimStatus.AwaitingInfo, "Reserve can only change on an open claim in review.");
        Require(amount >= 0, "Reserve cannot be negative.");
        RequireCents(amount);
        var old = Reserve;
        Reserve = amount;
        Record(ClaimEventType.ReserveSet, officerId, now, Status, Format(old), Format(amount), null, old);
    }

    public void Settle(Guid officerId, decimal amount, DateTimeOffset now)
    {
        RequireAssignedOfficer(officerId);
        Require(Status == ClaimStatus.InReview, "Only a claim in review can be settled.");
        Require(amount > 0, "Settlement amount must be greater than zero.");
        RequireCents(amount);
        // A higher payout needs a recorded reserve increase first, so exposure is never understated.
        Require(amount <= Reserve, "Settlement cannot exceed the reserve. Raise the reserve first.");
        SettledAmount = amount;
        ClosedAt = now;
        ChangeStatus(ClaimEventType.Settled, officerId, ClaimStatus.Settled, $"{Format(amount)} {Currency}", now);
    }

    public void Reject(Guid officerId, string reason, DateTimeOffset now)
    {
        RequireAssignedOfficer(officerId);
        // AwaitingInfo allowed too: a claimant who never answers must not keep the claim open forever.
        Require(Status is ClaimStatus.InReview or ClaimStatus.AwaitingInfo, "Only an open claim in review can be rejected.");
        RequireText(reason, "Reason");
        ClosedAt = now;
        ChangeStatus(ClaimEventType.Rejected, officerId, ClaimStatus.Rejected, reason, now);
    }

    // Clock runs from submit and does not pause during AwaitingInfo (assumption 8).
    public bool IsOverdue(DateTimeOffset now, int targetDays) =>
        Status is not (ClaimStatus.Settled or ClaimStatus.Rejected)
        && now - SubmittedAt > TimeSpan.FromDays(targetDays);

    public void ClearDomainEvents() => _events.Clear();

    void ChangeStatus(ClaimEventType type, Guid actorId, ClaimStatus to, string? note, DateTimeOffset now)
    {
        var from = Status;
        Status = to;
        Record(type, actorId, now, from, from.ToString(), to.ToString(), note, Reserve);
    }

    void Record(ClaimEventType type, Guid actorId, DateTimeOffset now, ClaimStatus? oldStatus,
        string? oldValue, string newValue, string? note, decimal oldReserve)
    {
        Version++;
        _history.Add(new ClaimHistory(Id, type, oldValue, newValue, note, actorId, now));
        _events.Add(new ClaimEvent(type, Id, actorId, oldStatus, Status, Market, Type, Currency,
            oldReserve, Reserve, SettledAmount, now));
    }

    void RequireAssignedOfficer(Guid officerId) =>
        Require(AssignedOfficerId == officerId, "Only the assigned officer can do this.");

    static void RequireText(string? text, string field) =>
        Require(!string.IsNullOrWhiteSpace(text), $"{field} is required.");

    // Money is stored as numeric(18,2); refuse values the database would round.
    static void RequireCents(decimal amount) =>
        Require(decimal.Round(amount, 2) == amount, "Amount can have at most 2 decimal places.");

    static void Require(bool ok, string message)
    {
        if (!ok) throw new DomainException(message);
    }

    static string Format(decimal amount) => amount.ToString(CultureInfo.InvariantCulture);
}
