using Claims.Api.Data;
using Claims.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Controllers;

[ApiController]
[Route("api/claims")]
public class ClaimsController(AppDbContext db, IConfiguration config) : ControllerBase
{
    [HttpPost, Authorize("ClaimantOnly"), EnableRateLimiting("submit")]
    public async Task<ActionResult<ClaimDetail>> Submit(SubmitClaimRequest r)
    {
        var claim = Claim.Submit(this.CurrentUserId(), r.Type!.Value, r.Market!.Value, r.PolicyNumber, r.Description,
            DateTimeOffset.UtcNow);
        db.Claims.Add(claim);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = claim.Id }, ToDetail(claim));
    }

    // shortcut: no paging, add it when queues grow past a few hundred claims.
    [HttpGet]
    public async Task<List<ClaimSummary>> List(ClaimQueue? queue, ClaimStatus? status)
    {
        var q = Visible().AsNoTracking();
        if (queue == ClaimQueue.Unassigned)
            q = q.Where(c => c.Status == ClaimStatus.Submitted && c.AssignedOfficerId == null);
        if (queue == ClaimQueue.Mine)
        {
            var me = this.CurrentUserId();
            q = q.Where(c => c.AssignedOfficerId == me || c.ClaimantId == me); // claimants are already filtered
        }
        if (status is not null) q = q.Where(c => c.Status == status);

        var claims = await q.OrderBy(c => c.SubmittedAt).ToListAsync();
        var now = DateTimeOffset.UtcNow;
        return claims.Select(c => ToSummary(c, now)).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClaimDetail>> Get(Guid id)
    {
        var claim = await Visible().AsNoTracking().Include(c => c.History).FirstOrDefaultAsync(c => c.Id == id);
        return claim is null ? NotFound() : ToDetail(claim);
    }

    [HttpPost("{id:guid}/assign"), Authorize("StaffOnly")]
    public Task<IActionResult> Assign(Guid id) =>
        Act(id, c => c.Assign(this.CurrentUserId(), DateTimeOffset.UtcNow));

    [HttpPost("{id:guid}/request-info"), Authorize("StaffOnly")]
    public Task<IActionResult> RequestInfo(Guid id, RequestInfoRequest r) =>
        Act(id, c => c.RequestInfo(this.CurrentUserId(), r.Question, DateTimeOffset.UtcNow));

    [HttpPost("{id:guid}/provide-info"), Authorize("ClaimantOnly")]
    public Task<IActionResult> ProvideInfo(Guid id, ProvideInfoRequest r) =>
        Act(id, c => c.ProvideInfo(this.CurrentUserId(), r.Answer, DateTimeOffset.UtcNow));

    [HttpPut("{id:guid}/reserve"), Authorize("StaffOnly")]
    public Task<IActionResult> SetReserve(Guid id, ReserveRequest r) =>
        Act(id, c => c.SetReserve(this.CurrentUserId(), r.Amount!.Value, DateTimeOffset.UtcNow));

    [HttpPost("{id:guid}/settle"), Authorize("StaffOnly")]
    public Task<IActionResult> Settle(Guid id, SettleRequest r) =>
        Act(id, c => c.Settle(this.CurrentUserId(), r.Amount!.Value, DateTimeOffset.UtcNow));

    [HttpPost("{id:guid}/reject"), Authorize("StaffOnly")]
    public Task<IActionResult> Reject(Guid id, RejectRequest r) =>
        Act(id, c => c.Reject(this.CurrentUserId(), r.Reason, DateTimeOffset.UtcNow));

    // Ownership: a claimant only ever sees their own claims, so anyone else's is 404 (no hint that it exists).
    // Staff see all claims; each Claim method still checks that the acting officer is the assigned one.
    // Any other role (or none) sees nothing: deny by default.
    IQueryable<Claim> Visible()
    {
        if (User.IsInRole(nameof(UserRole.Officer)) || User.IsInRole(nameof(UserRole.Manager))) return db.Claims;
        if (!User.IsInRole(nameof(UserRole.Claimant))) return db.Claims.Where(_ => false);
        var me = this.CurrentUserId();
        return db.Claims.Where(c => c.ClaimantId == me);
    }

    // Load → Claim method → save. Never ExecuteUpdateAsync/raw SQL: it would skip the rules, history and events.
    // Two officers picking up at once: the second save fails the Version check → 409 (ApiExceptionHandler).
    async Task<IActionResult> Act(Guid id, Action<Claim> action)
    {
        var claim = await Visible().FirstOrDefaultAsync(c => c.Id == id);
        if (claim is null) return NotFound();
        action(claim);
        await db.SaveChangesAsync();
        return NoContent();
    }

    ClaimSummary ToSummary(Claim c, DateTimeOffset now) => new(
        c.Id, c.Type, c.Market, c.Currency, c.Status, c.Reserve, c.SettledAmount, c.ClaimantId, c.AssignedOfficerId,
        c.SubmittedAt, c.ClosedAt, (int)((c.ClosedAt ?? now) - c.SubmittedAt).TotalDays,
        c.IsOverdue(now, config.GetValue<int>($"OverdueDays:{c.Type}")));

    ClaimDetail ToDetail(Claim c) => new(
        ToSummary(c, DateTimeOffset.UtcNow), c.PolicyNumber, c.Description,
        c.History.OrderBy(h => h.At)
            .Select(h => new TimelineItem(h.EventType, h.OldValue, h.NewValue, h.Note, h.ActorId, h.At)).ToList());
}

[ApiController]
[Route("api/notifications")]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<List<NotificationItem>> List()
    {
        var me = this.CurrentUserId();
        return await db.Notifications.AsNoTracking().Where(n => n.UserId == me).OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationItem(n.Id, n.ClaimId, n.Message, n.CreatedAt)).ToListAsync();
    }
}

public static class CurrentUser
{
    // The fallback policy guarantees a validated token, and dev-login always puts the user id in `sub`.
    // shortcut: missing/non-GUID `sub` throws → 500; return 401 instead once a real identity provider issues tokens.
    public static Guid CurrentUserId(this ControllerBase c) => Guid.Parse(c.User.FindFirst("sub")!.Value);
}
