namespace GameInventoryApi.DTOs;

public record RewardProgressDto(
    string Id,
    string RuleId,
    int CurrentCount,
    bool IsCompleted,
    DateTime? CompletedAt,
    bool IsReceived,
    DateTime? ReceivedAt
);
