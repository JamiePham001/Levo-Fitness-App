namespace backend.Entities.Goal;

public class Goal : SyncEntity
{
    // Client-generated so goals can be created offline.
    public Guid Id { get; private set; }
    // Owner: Supabase user id from the JWT "sub" claim, never taken from the payload.
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = default!;
    public double? Weight { get; private set; }
    public int? Reps { get; private set; }
    public Level Level { get; private set; }

    public static Goal Create(Guid userId, GoalSync dto, Guid deviceId, DateTime now)
    {
        if (dto.Id == Guid.Empty || userId == Guid.Empty) throw new ArgumentException("Id cannot be empty.");
        var goal = new Goal { Id = dto.Id, UserId = userId };
        goal.ApplySync(dto, deviceId, now);
        return goal;
    }

    // Overwrites every client-owned field from the snapshot. Returns false if the snapshot is stale.
    public bool ApplySync(GoalSync dto, Guid deviceId, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name cannot be empty.");
        if (dto.Weight is <= 0) throw new ArgumentException("Weight must be greater than 0.");
        if (dto.Reps is <= 0) throw new ArgumentException("Reps must be greater than 0.");
        if (!Enum.IsDefined(dto.Level)) throw new ArgumentException("Invalid level.");
        if (!BeginSync(dto.ClientUpdateAt, deviceId, now)) return false;

        Name = dto.Name.Trim();
        Weight = dto.Weight;
        Reps = dto.Reps;
        Level = dto.Level;
        DeletedAt = dto.DeletedAt;
        return true;
    }
}

public record GoalSync(Guid Id, string Name, double? Weight, int? Reps, Level Level, DateTime ClientUpdateAt, DateTime? DeletedAt);

public enum Level { Beginner, Intermediate, Advanced, Pro, Mutant };
