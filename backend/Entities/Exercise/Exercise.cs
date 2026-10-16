namespace backend.Entities.Exercises;

public class Exercise : SyncEntity
{
    // Client-generated so exercises can be created offline.
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    // Parent workout; fixed at creation.
    public Guid WorkoutId { get; private set; }
    // Soft reference (no foreign key): Name below is a snapshot, so the exercise survives its movement being purged.
    public Guid MovementId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Notes { get; private set; }
    public int Order { get; private set; }
    public bool Completed { get; private set; }

    public static Exercise Create(Guid userId, ExerciseSync dto, Guid deviceId, DateTime now)
    {
        Guard.NotEmpty(dto.Id, "Id");
        Guard.NotEmpty(userId, "UserId");
        Guard.NotEmpty(dto.WorkoutId, "WorkoutId");
        var exercise = new Exercise { Id = dto.Id, UserId = userId, WorkoutId = dto.WorkoutId };
        exercise.ApplySync(dto, deviceId, now);
        return exercise;
    }

    public bool ApplySync(ExerciseSync dto, Guid deviceId, DateTime now)
    {
        if (dto.WorkoutId != WorkoutId) throw new ArgumentException("An exercise cannot move to another workout.");
        Guard.NotEmpty(dto.MovementId, "MovementId");
        var name = Guard.Text(dto.Name, 100, "Name");
        var notes = Guard.OptionalText(dto.Notes, 2000, "Notes");
        Guard.NonNegative(dto.Order, "Order");
        if (!BeginSync(dto.ClientUpdateAt, dto.DeletedAt, deviceId, now)) return false;

        MovementId = dto.MovementId;
        Name = name;
        Notes = notes;
        Order = dto.Order;
        Completed = dto.Completed;
        return true;
    }
}
