namespace GameInventoryApi.DTOs;

public record RewardConditionDto(string Field, string Op, string Value);

public record CreateRewardRuleDto(
    string ActionType,
    List<RewardConditionDto> Conditions,
    string? AccumulateField,
    int TargetCount,
    RewardDto Reward
);

public record UpdateRewardRuleDto(
    string ActionType,
    List<RewardConditionDto> Conditions,
    string? AccumulateField,
    int TargetCount,
    RewardDto Reward
);

public record RewardRuleDto(
    string Id,
    string ActionType,
    List<RewardConditionDto> Conditions,
    string? AccumulateField,
    int TargetCount,
    RewardDto Reward,
    bool IsActive
);
