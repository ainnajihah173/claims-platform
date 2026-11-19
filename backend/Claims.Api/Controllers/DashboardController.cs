using Claims.Api.Data;
using Claims.Api.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize("StaffOnly")]
public class DashboardController(AppDbContext db, IConfiguration config) : ControllerBase
{
    // Event-fed read model: lags the claims by about a second; AsOf = when that row last changed.
    [HttpGet("exposure")]
    public Task<List<ExposureRow>> Exposure() =>
        db.ExposureSummary.AsNoTracking()
            .OrderBy(x => x.Market).ThenBy(x => x.ClaimType)
            .Select(x => new ExposureRow(x.Market, x.Currency, x.ClaimType, x.OpenCount, x.TotalReserve, x.UpdatedAt))
            .ToListAsync();

    // Live query on claims, so it is always exact. Averages cover claims closed in the last 30 days.
    [HttpGet("workload")]
    public async Task<Workload> Workload()
    {
        var now = DateTimeOffset.UtcNow;
        var since = now.AddDays(-30);
        // shortcut: loads every open claim and last-30-day closed claim into memory to reuse Claim.IsOverdue;
        // move the counts to a SQL GROUP BY once there are tens of thousands of open claims.
        var claims = await db.Claims.AsNoTracking()
            .Where(c => c.AssignedOfficerId != null
                && (c.ClosedAt == null || c.ClosedAt >= since))
            .ToListAsync();
        var staff = await db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Officer || u.Role == UserRole.Manager)
            .OrderBy(u => u.Name).ToListAsync();
        var unassigned = await db.Claims.CountAsync(c => c.Status == ClaimStatus.Submitted && c.AssignedOfficerId == null);

        var officers = staff.Select(u =>
        {
            var mine = claims.Where(c => c.AssignedOfficerId == u.Id).ToList();
            var open = mine.Where(c => c.ClosedAt == null).ToList();
            var closed = mine.Where(c => c.ClosedAt != null).ToList();
            return new OfficerWorkload(
                u.Id, u.Name, open.Count,
                open.Count(c => c.Status == ClaimStatus.AwaitingInfo),
                open.Count(c => c.IsOverdue(now, config.GetValue<int>($"OverdueDays:{c.Type}"))),
                open.Count == 0 ? null : (int)(now - open.Min(c => c.SubmittedAt)).TotalDays,
                closed.Count,
                closed.Count == 0 ? null : Math.Round(closed.Average(c => (c.ClosedAt!.Value - c.SubmittedAt).TotalDays), 1));
        }).ToList();

        return new Workload(now, unassigned, officers);
    }
}
