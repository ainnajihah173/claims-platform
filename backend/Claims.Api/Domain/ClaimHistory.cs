namespace Claims.Api.Domain;

// Append-only timeline row. Status changes, reserve changes, assignment,
// questions (InfoRequested) and answers (InfoProvided) all live here.
public class ClaimHistory
{
    public Guid Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public ClaimEventType EventType { get; private set; }
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public string? Note { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset At { get; private set; }

    ClaimHistory() { } // EF Core

    internal ClaimHistory(Guid claimId, ClaimEventType eventType, string? oldValue, string? newValue,
        string? note, Guid actorId, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7();
        ClaimId = claimId;
        EventType = eventType;
        OldValue = oldValue;
        NewValue = newValue;
        Note = note;
        ActorId = actorId;
        At = at;
    }
}
