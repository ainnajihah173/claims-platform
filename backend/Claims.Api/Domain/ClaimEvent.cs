namespace Claims.Api.Domain;

// Domain event raised by Claim; becomes an outbox row / Kafka message later.
// No personal data: no notes, policy number or description.
public record ClaimEvent(
    ClaimEventType Type,
    Guid ClaimId,
    Guid ActorId,
    ClaimStatus? OldStatus,
    ClaimStatus NewStatus,
    Market Market,
    ClaimType ClaimType,
    string Currency,
    decimal OldReserve,
    decimal Reserve,
    decimal? SettledAmount,
    DateTimeOffset At);
