namespace backend.Entities.Workouts;

public class Workout : SyncEntity
{
    // Client-generated so workouts can be created offline.
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    // Parent block; null for a workout that is not part of a program. Fixed at creation.
    public Guid? BlockId { get; private set; }
    public int Order { get; private set; }

    public static Workout Create(Guid userId, WorkoutSync dto, Guid deviceId, DateTime now)
    {
        Guard.NotEmpty(dto.Id, "Id");
        Guard.NotEmpty(userId, "UserId");
        if (dto.BlockId == Guid.Empty) throw new ArgumentException("BlockId cannot be empty.");
        var workout = new Workout { Id = dto.Id, UserId = userId, BlockId = dto.BlockId };
        workout.ApplySync(dto, deviceId, now);
        return workout;
    }

    public bool ApplySync(WorkoutSync dto, Guid deviceId, DateTime now)
    {
        if (dto.BlockId != BlockId) throw new ArgumentException("A workout cannot move to another block.");
        Guard.NonNegative(dto.Order, "Order");
        if (!BeginSync(dto.ClientUpdateAt, dto.DeletedAt, deviceId, now)) return false;

        Order = dto.Order;
        return true;
    }
}
