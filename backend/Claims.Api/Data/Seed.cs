using Claims.Api.Domain;

namespace Claims.Api.Data;

// shortcut: EnsureCreated + seed at startup, no migrations; move to EF migrations before any shared or production database.
public static class Seed
{
    // Fixed ids so dev login (Step 4) and the Scalar demo can name them.
    public static readonly Guid Aisha = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static readonly Guid Ben = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public static readonly Guid Chen = Guid.Parse("00000000-0000-0000-0000-000000000011");
    public static readonly Guid Devi = Guid.Parse("00000000-0000-0000-0000-000000000012");
    public static readonly Guid Eric = Guid.Parse("00000000-0000-0000-0000-000000000013");
    public static readonly Guid Farah = Guid.Parse("00000000-0000-0000-0000-000000000021");

    public static void Run(AppDbContext db, DateTimeOffset now)
    {
        if (db.Users.Any()) return;

        db.Users.AddRange(
            new User(Aisha, "Aisha Claimant", UserRole.Claimant, null),
            new User(Ben, "Ben Claimant", UserRole.Claimant, null),
            new User(Chen, "Chen Officer", UserRole.Officer, "Motor"),
            new User(Devi, "Devi Officer", UserRole.Officer, "Motor"),
            new User(Eric, "Eric Officer", UserRole.Officer, "Property"),
            new User(Farah, "Farah Manager", UserRole.Manager, "Claims"));

        // Built through Claim methods so every claim has a real history. Ages chosen so some are
        // overdue (motor > 14 days, property > 30 days).
        Claim Submit(Guid who, ClaimType type, Market market, string policy, string text, int daysAgo) =>
            Claim.Submit(who, type, market, policy, text, now.AddDays(-daysAgo));
        DateTimeOffset At(Claim c, int hours) => c.SubmittedAt.AddHours(hours);

        var unassigned = Submit(Aisha, ClaimType.Motor, Market.MY, "MY-MOT-1001", "Scratched door in car park", 2);
        var unassignedOverdue = Submit(Ben, ClaimType.Property, Market.SG, "SG-PRP-2001", "Burst pipe flooded kitchen", 35);

        var inReview = Submit(Aisha, ClaimType.Motor, Market.AU, "AU-MOT-3001", "Rear-ended at traffic lights", 3);
        inReview.Assign(Chen, At(inReview, 2));
        inReview.SetReserve(Chen, 5000m, At(inReview, 3));

        var inReviewOverdue = Submit(Ben, ClaimType.Motor, Market.MY, "MY-MOT-1002", "Windscreen cracked by stone", 20);
        inReviewOverdue.Assign(Chen, At(inReviewOverdue, 4));
        inReviewOverdue.SetReserve(Chen, 1500m, At(inReviewOverdue, 5));

        var awaiting = Submit(Aisha, ClaimType.Property, Market.SG, "SG-PRP-2002", "Roof leak after storm", 10);
        awaiting.Assign(Devi, At(awaiting, 1));
        awaiting.SetReserve(Devi, 20000m, At(awaiting, 2));
        awaiting.RequestInfo(Devi, "Please send photos of the damage and the repair quote.", At(awaiting, 3));

        var answered = Submit(Ben, ClaimType.Motor, Market.AU, "AU-MOT-3002", "Hail damage to bonnet", 16);
        answered.Assign(Eric, At(answered, 2));
        answered.RequestInfo(Eric, "Where was the car parked?", At(answered, 3));
        answered.ProvideInfo(Ben, "In the open driveway at home.", At(answered, 30));

        var settled = Submit(Aisha, ClaimType.Motor, Market.AU, "AU-MOT-3003", "Side mirror knocked off", 25);
        settled.Assign(Devi, At(settled, 1));
        settled.SetReserve(Devi, 1200m, At(settled, 2));
        settled.Settle(Devi, 1000m, At(settled, 48));

        var rejected = Submit(Ben, ClaimType.Property, Market.MY, "MY-PRP-1003", "Wear and tear on carpet", 12);
        rejected.Assign(Eric, At(rejected, 1));
        rejected.Reject(Eric, "Wear and tear is not covered by the policy.", At(rejected, 24));

        db.Claims.AddRange(unassigned, unassignedOverdue, inReview, inReviewOverdue, awaiting, answered, settled, rejected);
        db.SaveChanges();
    }
}
