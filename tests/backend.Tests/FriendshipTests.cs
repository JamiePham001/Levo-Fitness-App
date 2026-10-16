using backend.Entities.Friendships;
using static backend.Tests.TestData;

namespace backend.Tests;

public class FriendshipTests
{
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Receiver = Guid.NewGuid();
    private static readonly Guid Stranger = Guid.NewGuid();

    private static Friendship NewFriendship() => Friendship.Create(Requester, Receiver, T0);

    [Fact]
    public void Create_StartsPendingWithAServerGeneratedId()
    {
        var friendship = NewFriendship();

        Assert.NotEqual(Guid.Empty, friendship.Id);
        Assert.Equal(Requester, friendship.RequesterId);
        Assert.Equal(Receiver, friendship.ReceiverId);
        Assert.Equal(FriendshipStatus.Pending, friendship.Status);
        Assert.Equal(T0, friendship.CreatedAt);
        Assert.Null(friendship.BlockedById);
    }

    [Fact]
    public void Create_RejectsEmptyIdsAndSelfFriendship()
    {
        Assert.Throws<ArgumentException>(() => Friendship.Create(Guid.Empty, Receiver, T0));
        Assert.Throws<ArgumentException>(() => Friendship.Create(Requester, Guid.Empty, T0));
        Assert.Throws<ArgumentException>(() => Friendship.Create(Requester, Requester, T0));
    }

    [Fact]
    public void Accept_ByTheReceiver_AcceptsAPendingRequest()
    {
        var friendship = NewFriendship();

        friendship.Accept(Receiver);

        Assert.Equal(FriendshipStatus.Accepted, friendship.Status);
    }

    [Fact]
    public void Accept_ByAnyoneElse_IsRejected()
    {
        var friendship = NewFriendship();

        Assert.Throws<ArgumentException>(() => friendship.Accept(Requester));
        Assert.Throws<ArgumentException>(() => friendship.Accept(Stranger));
        Assert.Equal(FriendshipStatus.Pending, friendship.Status);
    }

    [Fact]
    public void Accept_WhenNotPending_IsRejected()
    {
        var accepted = NewFriendship();
        accepted.Accept(Receiver);
        var blocked = NewFriendship();
        blocked.Block(Requester);

        Assert.Throws<InvalidOperationException>(() => accepted.Accept(Receiver));
        Assert.Throws<InvalidOperationException>(() => blocked.Accept(Receiver));
        Assert.Equal(FriendshipStatus.Blocked, blocked.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Block_ByEitherParticipant_RecordsTheBlocker(bool byRequester)
    {
        var friendship = NewFriendship();
        var actor = byRequester ? Requester : Receiver;

        friendship.Block(actor);

        Assert.Equal(FriendshipStatus.Blocked, friendship.Status);
        Assert.Equal(actor, friendship.BlockedById);
    }

    [Fact]
    public void Block_ByAStranger_IsRejected()
    {
        var friendship = NewFriendship();

        Assert.Throws<ArgumentException>(() => friendship.Block(Stranger));
        Assert.Equal(FriendshipStatus.Pending, friendship.Status);
    }

    [Fact]
    public void Block_AfterAcceptance_Works()
    {
        var friendship = NewFriendship();
        friendship.Accept(Receiver);

        friendship.Block(Receiver);

        Assert.Equal(FriendshipStatus.Blocked, friendship.Status);
    }

    [Fact]
    public void Block_Twice_KeepsTheOriginalBlocker()
    {
        var friendship = NewFriendship();
        friendship.Block(Requester);

        friendship.Block(Receiver);

        Assert.Equal(Requester, friendship.BlockedById);
    }

    [Fact]
    public void CanRemove_ParticipantsCanRemoveUnblockedFriendships()
    {
        var friendship = NewFriendship();

        Assert.True(friendship.CanRemove(Requester));
        Assert.True(friendship.CanRemove(Receiver));
        Assert.False(friendship.CanRemove(Stranger));
    }

    [Fact]
    public void CanRemove_OnlyTheBlockerCanLiftABlock()
    {
        var friendship = NewFriendship();
        friendship.Block(Requester);

        Assert.True(friendship.CanRemove(Requester));
        Assert.False(friendship.CanRemove(Receiver));
        Assert.False(friendship.CanRemove(Stranger));
    }
}
