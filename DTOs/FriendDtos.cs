namespace GameInventoryApi.DTOs;

public record FriendProfileDto(
    string PlayerId,
    string Username,
    int Level,
    bool IsOnline,
    string Status = "idle", // "idle", "in_match", "offline"
    string Signature = ""
);

public record FriendRequestDto(
    string RequestId,
    string SenderPlayerId,
    string SenderUsername,
    int SenderLevel,
    DateTime CreatedAt
);

public record SendFriendRequestDto(string TargetUsernameOrId);

public record SendMatchInviteDto(string TargetPlayerId);

public record MatchInviteNotificationDto(
    string InviteId,
    string SenderPlayerId,
    string SenderUsername,
    int ExpiresInSeconds = 30
);

public record MatchInviteResponseDto(
    string InviteId,
    string Status // "accepted", "declined", "cancelled", "expired"
);

public record FriendPresenceUpdateDto(
    string PlayerId,
    bool IsOnline,
    string Status = "idle"
);

public record SearchPlayerResultDto(
    string PlayerId,
    string Username,
    int Level,
    string Relationship, // "none", "friend", "pending_sent", "pending_received"
    bool IsOnline = false,
    string Status = "offline"
);
