namespace backend.Entities.Programs;

public record ProgramSync(Guid Id, string Name, ProgramType Type, DateTime ClientUpdateAt, DateTime? DeletedAt);
