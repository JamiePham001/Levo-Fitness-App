namespace backend.Entities.Workouts;

public record WorkoutSync(Guid Id, int Order, Guid? BlockId, DateTime? DeletedAt, DateTime ClientUpdateAt);
