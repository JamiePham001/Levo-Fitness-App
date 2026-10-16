namespace backend.Entities.Movements;

public record MovementSync(Guid Id, string Name, string[]? Keywords, DateTime ClientUpdateAt, DateTime? DeletedAt);
