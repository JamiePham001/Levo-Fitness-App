namespace backend.Entities.Goals;

public record GoalSync(Guid Id, string Name, double? Weight, int? Reps, Level Level, DateTime ClientUpdateAt, DateTime? DeletedAt);
