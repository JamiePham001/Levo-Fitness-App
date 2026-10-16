using backend.Entities.Movements;
using static backend.Tests.TestData;

namespace backend.Tests;

public class MovementSyncTests
{
    private static MovementSync Dto(Guid? id = null, string name = "Back Squat", string[]? keywords = null,
        DateTime? clientUpdateAt = null, DateTime? deletedAt = null)
        => new(id ?? Guid.NewGuid(), name, keywords, clientUpdateAt ?? T0, deletedAt);

    [Fact]
    public void Create_CopiesSnapshotAndOwner()
    {
        var dto = Dto(name: "  Bench Press ", keywords: ["chest"]);

        var movement = Movement.Create(UserId, dto, DeviceA, T0);

        Assert.Equal(dto.Id, movement.Id);
        Assert.Equal(UserId, movement.UserId);
        Assert.Equal("Bench Press", movement.Name);
        Assert.Equal(["chest"], movement.Keywords);
    }

    [Fact]
    public void Keywords_AreTrimmedLowerCasedDeduplicatedAndBlanksDropped()
    {
        var movement = Movement.Create(UserId, Dto(keywords: [" Legs ", "legs", "", "  ", "QUADS"]), DeviceA, T0);

        Assert.Equal(["legs", "quads"], movement.Keywords);
    }

    [Fact]
    public void NullKeywords_BecomeEmpty()
    {
        var movement = Movement.Create(UserId, Dto(keywords: null), DeviceA, T0);

        Assert.Empty(movement.Keywords);
    }

    [Fact]
    public void ApplySync_NullKeywords_ClearsExistingKeywords()
    {
        var id = Guid.NewGuid();
        var movement = Movement.Create(UserId, Dto(id, keywords: ["legs"]), DeviceA, T0);

        movement.ApplySync(Dto(id, keywords: null, clientUpdateAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1));

        Assert.Empty(movement.Keywords);
    }

    [Fact]
    public void TooManyKeywords_IsRejected()
    {
        var keywords = Enumerable.Range(0, 21).Select(i => $"k{i}").ToArray();

        Assert.Throws<ArgumentException>(() => Movement.Create(UserId, Dto(keywords: keywords), DeviceA, T0));
    }

    [Fact]
    public void TooLongKeyword_IsRejected()
        => Assert.Throws<ArgumentException>(() => Movement.Create(UserId, Dto(keywords: [new string('a', 51)]), DeviceA, T0));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankName_IsRejected(string name)
        => Assert.Throws<ArgumentException>(() => Movement.Create(UserId, Dto(name: name), DeviceA, T0));

    [Fact]
    public void NameLongerThanColumn_IsRejected()
        => Assert.Throws<ArgumentException>(() => Movement.Create(UserId, Dto(name: new string('a', 101)), DeviceA, T0));

    [Fact]
    public void Create_RejectsEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => Movement.Create(UserId, Dto(id: Guid.Empty), DeviceA, T0));
        Assert.Throws<ArgumentException>(() => Movement.Create(Guid.Empty, Dto(), DeviceA, T0));
    }

    [Fact]
    public void DeleteSnapshot_SetsTombstone()
    {
        var id = Guid.NewGuid();
        var movement = Movement.Create(UserId, Dto(id), DeviceA, T0);

        movement.ApplySync(Dto(id, clientUpdateAt: T0.AddMinutes(1), deletedAt: T0.AddMinutes(1)), DeviceA, T0.AddMinutes(1));

        Assert.Equal(T0.AddMinutes(1), movement.DeletedAt);
    }

    [Fact]
    public void StaleSnapshot_ChangesNothing()
    {
        var id = Guid.NewGuid();
        var movement = Movement.Create(UserId, Dto(id, name: "Squat", keywords: ["legs"]), DeviceA, T0);

        var applied = movement.ApplySync(Dto(id, name: "Stale", keywords: [], clientUpdateAt: T0), DeviceB, T0.AddMinutes(1));

        Assert.False(applied);
        Assert.Equal("Squat", movement.Name);
        Assert.Equal(["legs"], movement.Keywords);
    }
}
