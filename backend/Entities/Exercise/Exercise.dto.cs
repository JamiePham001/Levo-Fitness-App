namespace backend.Entities.Exercises;

public record ExerciseSync(Guid Id, Guid WorkoutId, Guid MovementId, string Name, string? Notes, int Order, bool Completed, DateTime ClientUpdateAt, DateTime? DeletedAt);
