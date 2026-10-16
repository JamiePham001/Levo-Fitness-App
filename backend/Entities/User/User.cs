namespace backend.Entities.Users;

public class User : SyncEntity
{
    // Supabase auth.users.id (the JWT "sub" claim); set on first sign-in, never generated here.
    public Guid Id { get; private set; }
    public string Username { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public Gender Gender { get; private set; }
    public double Weight { get; private set; }
    // Apple often provides no avatar.
    public string? AvatarUrl { get; private set; }
    // "google" or "apple" (JWT app_metadata.provider)
    public string Provider { get; private set; } = default!;
    public Measurement Measurement { get; private set; } = Measurement.Metric;
    public Role Role { get; private set; } = Role.Member;

    // Username, email, provider and avatar come from the JWT and request, the rest from the sync snapshot.
    public static User CreateOAuth(UserSync dto, string username, string email, string? avatarUrl, string provider, Guid deviceId, DateTime now)
    {
        Guard.NotEmpty(dto.Id, "Id");
        var providerName = provider?.Trim().ToLowerInvariant();
        if (providerName is not ("google" or "apple")) throw new ArgumentException("Provider must be google or apple.");

        var user = new User
        {
            Id = dto.Id,
            Username = Guard.Text(username, 30, "Username"),
            Email = Guard.Text(email, 320, "Email"),
            AvatarUrl = Guard.OptionalText(avatarUrl, 2048, "AvatarUrl"),
            Provider = providerName
        };
        user.ApplySync(dto, deviceId, now);
        return user;
    }

    // Only the client-owned profile fields. Identity fields and Role are server-owned, and a user
    // cannot tombstone their own profile through sync (account deletion has its own endpoint).
    public bool ApplySync(UserSync dto, Guid deviceId, DateTime now)
    {
        Guard.Defined(dto.Gender, "Gender");
        Guard.Defined(dto.Measurement, "Measurement");
        Guard.Positive(dto.Weight, "Weight");
        if (!BeginSync(dto.ClientUpdateAt, null, deviceId, now)) return false;

        Gender = dto.Gender;
        Weight = dto.Weight;
        Measurement = dto.Measurement;
        return true;
    }

    public void UpdateRole(Role role)
    {
        Guard.Defined(role, "Role");
        Role = role;
    }
}

public enum Gender { Male, Female };
public enum Role { Member, Premium, Admin };
public enum Measurement { Metric, Imperial };
