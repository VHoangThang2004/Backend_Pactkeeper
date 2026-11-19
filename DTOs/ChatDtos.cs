namespace GameInventoryApi.DTOs;

public record ChatMessageDto(
    string Id,
    string SenderId,
    string SenderUsername,
    string ReceiverId,
    string Text,
    bool IsRead,
    DateTime CreatedAt
);

public record SendMessageRequestDto(
    string ReceiverId,
    string Text
);

public record ChatConversationDto(
    string FriendPlayerId,
    string FriendUsername,
    int FriendLevel,
    bool IsOnline,
    string Status,
    string LatestMessageText,
    DateTime? LatestMessageTime,
    int UnreadCount
);

public record ChatUnreadCountDto(
    int TotalUnreadCount
);
