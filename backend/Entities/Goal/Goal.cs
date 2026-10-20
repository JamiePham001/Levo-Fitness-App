namespace backend.Entities.Goals;

public class Goal : SyncEntity
{
    // Client-generated so goals can be created offline.
    public Guid Id { get; private set; }
    // Owner: Supabase user id from the JWT "sub" claim, never taken from the payload.
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = default!;
    public double? Weight { get; private set; }
    public int? Reps { get; private set; }
    public Level Level { get; private set; } = Level.None;

    public static Goal Create(Guid userId, GoalSync dto, Guid deviceId, DateTime now)
    {
        Guard.NotEmpty(dto.Id, "Id");
        Guard.NotEmpty(userId, "UserId");
        var goal = new Goal { Id = dto.Id, UserId = userId };
        goal.ApplySync(dto, deviceId, now);
        return goal;
    }

    // The snapshot sent to clients: the inverse of ApplySync.
    public GoalSync ToSync() => new(Id: Id, Name: Name, Weight: Weight, Reps: Reps, Level: Level, ClientUpdateAt: ClientUpdateAt, DeletedAt: DeletedAt);

    // Overwrites every client-owned field from the snapshot. Returns false if the snapshot is stale.
    public bool ApplySync(GoalSync dto, Guid deviceId, DateTime now)
    {
        var name = Guard.Text(dto.Name, 100, "Name");
        Guard.Positive(dto.Weight, "Weight");
        Guard.Positive(dto.Reps, "Reps");
        Guard.Defined(dto.Level, "Level");
        if (!BeginSync(dto.ClientUpdateAt, dto.DeletedAt, deviceId, now)) return false;

        Name = name;
        Weight = dto.Weight;
        Reps = dto.Reps;
        Level = dto.Level;
        return true;
    }
}

public enum Level { None, Beginner, Intermediate, Advanced, Pro, Mutant };
