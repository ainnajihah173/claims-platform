namespace Claims.Api.Domain;

public enum UserRole { Claimant, Officer, Manager }

public class User(Guid id, string name, UserRole role, string? team)
{
    public Guid Id { get; private set; } = id;
    public string Name { get; private set; } = name;
    public UserRole Role { get; private set; } = role;
    public string? Team { get; private set; } = team; // officers and managers only (assumption 6)
}

// In-app message to a claimant about one claim. Written by the event consumer (Step 5).
public class Notification(Guid id, Guid userId, Guid claimId, string message, DateTimeOffset createdAt)
{
    public Guid Id { get; private set; } = id;
    public Guid UserId { get; private set; } = userId;
    public Guid ClaimId { get; private set; } = claimId;
    public string Message { get; private set; } = message;
    public DateTimeOffset CreatedAt { get; private set; } = createdAt;
}
