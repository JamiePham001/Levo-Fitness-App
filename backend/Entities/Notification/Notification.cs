namespace backend.Entities.Notifications;

// Online-only inbox pointer: "something happened, open TargetId". State lives on the target, not here.
public class Notification
{
    public Guid Id { get; private set; }
    public Guid ReceiverId { get; private set; }
    public Guid SenderId { get; private set; }
    public NotificationType Type { get; private set; }
    // What to open: a friendship or program share id, depending on Type.
    public Guid TargetId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    // Null while unread.
    public DateTime? ReadAt { get; private set; }

    public static Notification Create(Guid receiverId, Guid senderId, NotificationType type, Guid targetId, DateTime now)
    {
        Guard.NotEmpty(receiverId, "ReceiverId");
        Guard.NotEmpty(senderId, "SenderId");
        Guard.NotEmpty(targetId, "TargetId");
        if (receiverId == senderId) throw new ArgumentException("A user cannot notify themselves.");
        Guard.Defined(type, "Type");

        return new Notification
        {
            Id = Guid.NewGuid(),
            ReceiverId = receiverId,
            SenderId = senderId,
            Type = type,
            TargetId = targetId,
            CreatedAt = now
        };
    }

    // Idempotent: the first read time is kept.
    public void MarkRead(DateTime now) => ReadAt ??= now;
}

public enum NotificationType { FriendRequest, FriendRequestAccepted, SharedProgram }
