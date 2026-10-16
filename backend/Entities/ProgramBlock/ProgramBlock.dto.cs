namespace backend.Entities.ProgramBlocks;

public record BlockSync(Guid Id, Guid ProgramId, string? Name, int Order, DateTime? DeletedAt, DateTime ClientUpdateAt);
