namespace backend.Entities.User;

public class User
{
    // Supabase auth.users.id (the JWT "sub" claim); set on first sign-in, never generated here.
    public Guid Id { get; private set; }
    public string Username { get; private set; } = default!;
    public string Email { get; private set; } = default!;
    public Gender Gender { get; private set; } = default!;
    public double Weight { get; private set; } = default!;
    public string AvatarUrl { get; private set; } = default!;
    // "google" or "apple" (JWT app_metadata.provider)
    public string Provider { get; private set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Measurement Measurement { get; private set; } = Measurement.Metric;
    public Role Role { get; private set; } = Role.Member;

    public void NewUser(Guid id, string username, string email, Gender gender, double? weight)
    {
        if (username == null || email == null) throw new ArgumentException("Username and email cannot be null.");
        if (weight.Equals(0) || weight is null) throw new ArgumentException("Weight must be greater than 0.");
        if (gender != Gender.Male && gender != Gender.Female) throw new ArgumentException("Gender must be Male or Female.");
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.");

        Id = id;
        Username = username;
        Email = email;
        Gender = gender;
        Weight = (double)weight;
    }

    public void NewOAuthUser(Guid id, string username, string email, string avatarUrl, string provider, Gender gender, double? weight)
    {
        if (username == null || email == null) throw new ArgumentException("Username and email cannot be null.");
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.");
        if (weight.Equals(0) || weight is null) throw new ArgumentException("Weight must be greater than 0.");
        if (gender != Gender.Male && gender != Gender.Female) throw new ArgumentException("Gender must be Male or Female.");

        Id = id;
        Username = username;
        Email = email;
        AvatarUrl = avatarUrl;
        Provider = provider;
        Gender = gender;
        Weight = (double)weight;
    }
}

public enum Gender { Male, Female };
public enum Role { Member, Premium, Admin };
public enum Measurement { Metric, Imperial };
