using backend.Entities.ProgramShares;
using static backend.Tests.TestData;

namespace backend.Tests;

public class ProgramShareTests
{
    private static readonly Guid ProgramId = Guid.NewGuid();
    private static readonly Guid Receiver = Guid.NewGuid();
    private static readonly Guid Sender = Guid.NewGuid();

    [Theory]
    [InlineData(ShareAccess.Download)]
    [InlineData(ShareAccess.Collab)]
    public void Create_SetsEverythingAndGeneratesAnId(ShareAccess access)
    {
        var share = ProgramShare.Create(ProgramId, Receiver, Sender, access, T0);

        Assert.NotEqual(Guid.Empty, share.Id);
        Assert.Equal(ProgramId, share.ProgramId);
        Assert.Equal(Receiver, share.ReceiverId);
        Assert.Equal(Sender, share.SenderId);
        Assert.Equal(access, share.Access);
        Assert.Equal(T0, share.CreatedAt);
    }

    [Fact]
    public void Create_RejectsEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => ProgramShare.Create(Guid.Empty, Receiver, Sender, ShareAccess.Download, T0));
        Assert.Throws<ArgumentException>(() => ProgramShare.Create(ProgramId, Guid.Empty, Sender, ShareAccess.Download, T0));
        Assert.Throws<ArgumentException>(() => ProgramShare.Create(ProgramId, Receiver, Guid.Empty, ShareAccess.Download, T0));
    }

    [Fact]
    public void Create_RejectsSharingWithYourself()
        => Assert.Throws<ArgumentException>(() => ProgramShare.Create(ProgramId, Sender, Sender, ShareAccess.Download, T0));

    [Fact]
    public void Create_RejectsUndefinedAccess()
        => Assert.Throws<ArgumentException>(() => ProgramShare.Create(ProgramId, Receiver, Sender, (ShareAccess)9, T0));
}
