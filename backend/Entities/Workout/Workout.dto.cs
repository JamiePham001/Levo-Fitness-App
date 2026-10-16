namespace backend.Entities.Workouts;

public record WorkoutSync(Guid Id, string? Name, int Order, Guid? BlockId, DateTime CreatedAt, DateTime? DeletedAt, DateTime ClientUpdateAt);
