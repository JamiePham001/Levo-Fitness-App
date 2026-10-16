using backend.Entities.Notifications;
using static backend.Tests.TestData;

namespace backend.Tests;

public class NotificationTests
{
    private static readonly Guid Receiver = Guid.NewGuid();
    private static readonly Guid Sender = Guid.NewGuid();
    private static readonly Guid Target = Guid.NewGuid();

    [Theory]
    [InlineData(NotificationType.FriendRequest)]  // the default enum value must be accepted
    [InlineData(NotificationType.FriendRequestAccepted)]
    [InlineData(NotificationType.SharedProgram)]
    public void Create_AcceptsEveryType(NotificationType type)
    {
        var notification = Notification.Create(Receiver, Sender, type, Target, T0);

        Assert.NotEqual(Guid.Empty, notification.Id);
        Assert.Equal(Receiver, notification.ReceiverId);
        Assert.Equal(Sender, notification.SenderId);
        Assert.Equal(type, notification.Type);
        Assert.Equal(Target, notification.TargetId);
        Assert.Equal(T0, notification.CreatedAt);
        Assert.Null(notification.ReadAt);
    }

    [Fact]
    public void Create_RejectsEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => Notification.Create(Guid.Empty, Sender, NotificationType.FriendRequest, Target, T0));
        Assert.Throws<ArgumentException>(() => Notification.Create(Receiver, Guid.Empty, NotificationType.FriendRequest, Target, T0));
        Assert.Throws<ArgumentException>(() => Notification.Create(Receiver, Sender, NotificationType.FriendRequest, Guid.Empty, T0));
    }

    [Fact]
    public void Create_RejectsNotifyingYourself()
        => Assert.Throws<ArgumentException>(() => Notification.Create(Receiver, Receiver, NotificationType.FriendRequest, Target, T0));

    [Fact]
    public void Create_RejectsUndefinedType()
        => Assert.Throws<ArgumentException>(() => Notification.Create(Receiver, Sender, (NotificationType)9, Target, T0));

    [Fact]
    public void MarkRead_SetsReadAtOnceAndKeepsTheFirstTime()
    {
        var notification = Notification.Create(Receiver, Sender, NotificationType.SharedProgram, Target, T0);

        notification.MarkRead(T0.AddMinutes(1));
        notification.MarkRead(T0.AddMinutes(9));

        Assert.Equal(T0.AddMinutes(1), notification.ReadAt);
    }
}
