using System.ComponentModel.DataAnnotations;
using Claims.Api.Domain;

namespace Claims.Api.Controllers;

// Requests carry only what the caller may choose. Ids of the acting user come from the
// current user, never from the body (blocks mass-assignment).
public record SubmitClaimRequest(
    [Required, EnumDataType(typeof(ClaimType))] ClaimType? Type,
    [Required, EnumDataType(typeof(Market))] Market? Market,
    [Required, StringLength(50)] string PolicyNumber,
    [Required, StringLength(2000)] string Description);

public record RequestInfoRequest([Required, StringLength(2000)] string Question);
public record ProvideInfoRequest([Required, StringLength(2000)] string Answer);
public record RejectRequest([Required, StringLength(2000)] string Reason);
public record ReserveRequest([Required, Money] decimal? Amount);
public record SettleRequest([Required, Money(Positive = true)] decimal? Amount);

public enum ClaimQueue { Unassigned, Mine }

public record ClaimSummary(
    Guid Id, ClaimType Type, Market Market, string Currency, ClaimStatus Status,
    decimal Reserve, decimal? SettledAmount, Guid ClaimantId, Guid? AssignedOfficerId,
    DateTimeOffset SubmittedAt, DateTimeOffset? ClosedAt, int AgeDays, bool IsOverdue);

public record ClaimDetail(ClaimSummary Claim, string PolicyNumber, string Description, List<TimelineItem> Timeline);

public record TimelineItem(ClaimEventType EventType, string? OldValue, string? NewValue, string? Note, Guid ActorId, DateTimeOffset At);

public record NotificationItem(Guid Id, Guid ClaimId, string Message, DateTimeOffset CreatedAt);

// Money fits numeric(18,2): not negative, at most 2 decimals, at most 16 digits before the point.
public sealed class MoneyAttribute : ValidationAttribute
{
    public bool Positive { get; init; }

    public override string FormatErrorMessage(string name) =>
        $"{name} must be {(Positive ? "greater than zero" : "zero or more")}, with at most 2 decimals.";

    public override bool IsValid(object? value) =>
        value is not decimal d
        || (d >= 0 && (!Positive || d > 0) && decimal.Round(d, 2) == d && d < 10_000_000_000_000_000m);
}
