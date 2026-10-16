using backend.Entities.Programs;
using static backend.Tests.TestData;
using ProgramEntity = backend.Entities.Programs.Program;

namespace backend.Tests;

public class ProgramSyncTests
{
    private static ProgramSync Dto(Guid? id = null, string name = "PPL", ProgramType type = ProgramType.Split,
        DateTime? clientUpdateAt = null, DateTime? deletedAt = null)
        => new(id ?? Guid.NewGuid(), name, type, clientUpdateAt ?? T0, deletedAt);

    [Theory]
    [InlineData(ProgramType.Split)]    // the default enum value must be accepted
    [InlineData(ProgramType.Program)]
    public void Create_AcceptsEveryProgramType(ProgramType type)
    {
        var dto = Dto(name: "  Upper Lower ", type: type);

        var program = ProgramEntity.Create(UserId, dto, DeviceA, T0);

        Assert.Equal(dto.Id, program.Id);
        Assert.Equal(UserId, program.UserId);
        Assert.Equal("Upper Lower", program.Name);
        Assert.Equal(type, program.Type);
        Assert.Equal(T0, program.ClientUpdateAt);
        Assert.Equal(DeviceA, program.UpdatedByDeviceId);
    }

    [Fact]
    public void Create_UsesTheClientTimestampNotServerTime()
    {
        var program = ProgramEntity.Create(UserId, Dto(clientUpdateAt: T0), DeviceA, T0.AddMinutes(1));

        Assert.Equal(T0, program.ClientUpdateAt);
        Assert.Equal(T0.AddMinutes(1), program.ServerUpdateAt);
    }

    [Fact]
    public void Create_RejectsEmptyIdsAndBadValues()
    {
        Assert.Throws<ArgumentException>(() => ProgramEntity.Create(UserId, Dto(id: Guid.Empty), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => ProgramEntity.Create(Guid.Empty, Dto(), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => ProgramEntity.Create(UserId, Dto(name: " "), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => ProgramEntity.Create(UserId, Dto(type: (ProgramType)99), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => ProgramEntity.Create(UserId, Dto(), Guid.Empty, T0));
    }

    [Fact]
    public void ApplySync_NewerSnapshot_UpdatesProgram()
    {
        var id = Guid.NewGuid();
        var program = ProgramEntity.Create(UserId, Dto(id), DeviceA, T0);

        var applied = program.ApplySync(Dto(id, name: "Bro Split", type: ProgramType.Program, clientUpdateAt: T0.AddMinutes(1)), DeviceB, T0.AddMinutes(1));

        Assert.True(applied);
        Assert.Equal("Bro Split", program.Name);
        Assert.Equal(ProgramType.Program, program.Type);
        Assert.Equal(DeviceB, program.UpdatedByDeviceId);
    }

    [Fact]
    public void ApplySync_StaleSnapshot_ChangesNothing()
    {
        var id = Guid.NewGuid();
        var program = ProgramEntity.Create(UserId, Dto(id, name: "PPL"), DeviceA, T0);

        var applied = program.ApplySync(Dto(id, name: "Stale", clientUpdateAt: T0), DeviceB, T0.AddMinutes(1));

        Assert.False(applied);
        Assert.Equal("PPL", program.Name);
    }

    [Fact]
    public void ApplySync_DeleteSnapshot_SetsTombstone()
    {
        var id = Guid.NewGuid();
        var program = ProgramEntity.Create(UserId, Dto(id), DeviceA, T0);

        program.ApplySync(Dto(id, clientUpdateAt: T0.AddMinutes(1), deletedAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1));

        Assert.Equal(T0.AddMinutes(1), program.DeletedAt);
    }
}
