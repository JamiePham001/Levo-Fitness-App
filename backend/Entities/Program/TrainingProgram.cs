namespace backend.Entities.Programs;

public class TrainingProgram : SyncEntity
{
    // Client-generated so programs can be created offline.
    public Guid Id { get; private set; }
    // Owner: Supabase user id from the JWT "sub" claim, never taken from the payload.
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = default!;
    public ProgramType Type { get; private set; } = ProgramType.Split;

    public static TrainingProgram Create(Guid userId, ProgramSync dto, Guid deviceId, DateTime now)
    {
        Guard.NotEmpty(dto.Id, "Id");
        Guard.NotEmpty(userId, "UserId");
        var program = new TrainingProgram { Id = dto.Id, UserId = userId };
        program.ApplySync(dto, deviceId, now);
        return program;
    }

    public bool ApplySync(ProgramSync dto, Guid deviceId, DateTime now)
    {
        var name = Guard.Text(dto.Name, 100, "Name");
        Guard.Defined(dto.Type, "Type");
        if (!BeginSync(dto.ClientUpdateAt, dto.DeletedAt, deviceId, now)) return false;

        Name = name;
        Type = dto.Type;
        return true;
    }
}

public enum ProgramType { Split, Program }
