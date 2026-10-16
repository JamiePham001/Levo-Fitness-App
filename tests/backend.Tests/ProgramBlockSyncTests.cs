using backend.Entities.ProgramBlocks;
using static backend.Tests.TestData;

namespace backend.Tests;

public class ProgramBlockSyncTests
{
    private static readonly Guid ProgramId = Guid.NewGuid();

    private static BlockSync Dto(Guid? id = null, Guid? programId = null, string? name = "Hypertrophy", int order = 0,
        DateTime? clientUpdateAt = null, DateTime? deletedAt = null)
        => new(id ?? Guid.NewGuid(), programId ?? ProgramId, name, order, deletedAt, clientUpdateAt ?? T0);

    [Fact]
    public void Create_SetsParentOwnerAndFields()
    {
        var dto = Dto(name: "  Week 1 ", order: 2);

        var block = ProgramBlock.Create(UserId, dto, DeviceA, T0);

        Assert.Equal(dto.Id, block.Id);
        Assert.Equal(UserId, block.UserId);
        Assert.Equal(ProgramId, block.ProgramId);
        Assert.Equal("Week 1", block.Name);
        Assert.Equal(2, block.Order);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Name_IsOptional(string? name)
    {
        var block = ProgramBlock.Create(UserId, Dto(name: name), DeviceA, T0);

        Assert.Null(block.Name);
    }

    [Fact]
    public void Create_RejectsEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => ProgramBlock.Create(UserId, Dto(id: Guid.Empty), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => ProgramBlock.Create(Guid.Empty, Dto(), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => ProgramBlock.Create(UserId, Dto(programId: Guid.Empty), DeviceA, T0));
    }

    [Fact]
    public void NegativeOrder_IsRejected()
        => Assert.Throws<ArgumentException>(() => ProgramBlock.Create(UserId, Dto(order: -1), DeviceA, T0));

    [Fact]
    public void NameLongerThanColumn_IsRejected()
        => Assert.Throws<ArgumentException>(() => ProgramBlock.Create(UserId, Dto(name: new string('a', 101)), DeviceA, T0));

    [Fact]
    public void ApplySync_CannotMoveBlockToAnotherProgram()
    {
        var id = Guid.NewGuid();
        var block = ProgramBlock.Create(UserId, Dto(id), DeviceA, T0);

        Assert.Throws<ArgumentException>(() =>
            block.ApplySync(Dto(id, programId: Guid.NewGuid(), clientUpdateAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1)));
        Assert.Equal(ProgramId, block.ProgramId);
    }

    [Fact]
    public void ApplySync_NewerSnapshot_UpdatesAndClearsName()
    {
        var id = Guid.NewGuid();
        var block = ProgramBlock.Create(UserId, Dto(id, name: "Old", order: 0), DeviceA, T0);

        block.ApplySync(Dto(id, name: null, order: 3, clientUpdateAt: T0.AddMinutes(1)), DeviceB, T0.AddMinutes(1));

        Assert.Null(block.Name);
        Assert.Equal(3, block.Order);
        Assert.Equal(DeviceB, block.UpdatedByDeviceId);
    }

    [Fact]
    public void ApplySync_StaleSnapshot_ChangesNothing()
    {
        var id = Guid.NewGuid();
        var block = ProgramBlock.Create(UserId, Dto(id, name: "Keep"), DeviceA, T0);

        var applied = block.ApplySync(Dto(id, name: "Stale", clientUpdateAt: T0), DeviceB, T0.AddMinutes(1));

        Assert.False(applied);
        Assert.Equal("Keep", block.Name);
    }

    [Fact]
    public void ApplySync_DeleteSnapshot_SetsTombstone()
    {
        var id = Guid.NewGuid();
        var block = ProgramBlock.Create(UserId, Dto(id), DeviceA, T0);

        block.ApplySync(Dto(id, clientUpdateAt: T0.AddMinutes(1), deletedAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1));

        Assert.Equal(T0.AddMinutes(1), block.DeletedAt);
    }
}
