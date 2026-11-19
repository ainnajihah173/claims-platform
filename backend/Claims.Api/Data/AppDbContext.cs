using Claims.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<ExposureSummary> ExposureSummary => Set<ExposureSummary>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Enums as text: reordering an enum never remaps saved rows.
        b.Properties<Enum>().HaveConversion<string>();
        // Every decimal here is money.
        b.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Claim>(e =>
        {
            e.ToTable("claims");
            e.Property(c => c.Id).ValueGeneratedNever();
            e.Property(c => c.Version).IsConcurrencyToken(); // second concurrent pick-up fails to save
            e.Ignore(c => c.DomainEvents);
            e.HasMany(c => c.History).WithOne().HasForeignKey(h => h.ClaimId);
            e.Navigation(c => c.History).HasField("_history");
            e.HasIndex(c => new { c.Status, c.AssignedOfficerId });
            e.HasIndex(c => c.ClaimantId);
        });
        // Ids are set in code; without this EF treats a new history row as existing and UPDATEs it.
        b.Entity<ClaimHistory>(e => { e.ToTable("claim_history"); e.Property(h => h.Id).ValueGeneratedNever(); });
        b.Entity<User>().ToTable("users");
        b.Entity<Notification>(e => { e.ToTable("notifications"); e.HasIndex(n => n.UserId); });
        b.Entity<OutboxMessage>(e => { e.ToTable("outbox"); e.Property(o => o.Payload).HasColumnType("jsonb"); });
        b.Entity<ProcessedEvent>(e => { e.ToTable("processed_events"); e.HasKey(p => p.EventId); });
        b.Entity<ExposureSummary>(e =>
        {
            e.ToTable("exposure_summary");
            e.HasKey(x => new { x.Market, x.Currency, x.ClaimType });
        });
    }
}

// Tables for Step 5 (outbox publisher + projection consumer).
public class OutboxMessage
{
    public Guid Id { get; set; } // = eventId
    public string EventType { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public class ProcessedEvent
{
    public Guid EventId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

public class ExposureSummary
{
    public Market Market { get; set; }
    public string Currency { get; set; } = "";
    public ClaimType ClaimType { get; set; }
    public int OpenCount { get; set; }
    public decimal TotalReserve { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
