namespace backend.Entities.ExerciseSets;

public record SetSync(Guid Id, Guid ExerciseId, int Order, double? Weight, int? Reps, double? RIR, double RestTime, bool Completed, DateTime? DeletedAt, DateTime ClientUpdateAt);
