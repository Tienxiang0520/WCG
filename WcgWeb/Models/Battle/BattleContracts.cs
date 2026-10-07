namespace WcgWeb.Models.Battle;

public record BattleCommand(Guid CommandId, Guid MatchId, long ExpectedRevision, string Type,
    Guid? InstanceId = null, Guid? TargetId = null, string? OptionId = null,
    string? PlayerDeckId = null, string? ComputerDeckId = null, bool PlayerFirst = true);
public record BattleCard(Guid InstanceId, string CardId, string Name, string Type, string Will,
    int Cost, int? PP, int? DP, string Text, string Art, string[]? Arrows = null);
public record BattleHand(BattleCard Card, bool CanPlay, bool CanEnergy, string Problem,
    string Preparation, string EnergyProblem, string[] PlayTargets, string Warning = "", bool CanSet = false);
public record BattleTarget(string Id, string Label, GameEnginePreview? Preview);
public record GameEnginePreview(bool AttackerDies, bool DefenderDies, bool AttackerShieldBreaks,
    bool DefenderShieldBreaks, int PlayerDamage);
public record BattleMonster(BattleCard Card, int PP, int DP, string[] Status,
    bool CanAttack, string Problem, BattleTarget[] Targets, BattleCard? Attachment, int Slot = -1, bool Tapped = false, bool IsSet = false, bool CanActivate = false, BattleCard[]? Attachments = null);
public record BattleEnergy(Guid InstanceId, bool Tapped);
public record BattleSide(string Id, int Hp, int DeckCount, int HandCount, int AvailableEnergy,
    int TotalEnergy, BattleEnergy[] Energy, BattleMonster[] Field, BattleCard[] Graveyard);
public record BattleOption(string Id, string Title, string Subtitle, BattleCard? Card);
public record BattlePending(string Kind, string Title, string Description, bool CanCancel,
    BattleOption[] Options, string[] Targets, Guid? SourceInstanceId = null);
public record BattleEvent(Guid Id, long Revision, int Order, string Type, string Side,
    Guid? InstanceId, Guid? TargetId, int Amount, BattleCard? Card, string Label);
public record BattleSnapshot(Guid MatchId, long Revision, int Turn, string Phase, string DecisionPlayerId,
    bool IsOver, string Outcome, BattleSide Player, BattleSide Computer, BattleHand[] Hand,
    BattlePending? Pending, string[] Logs, BattleCard[] RevealedCards, string RevealTitle, string ActivePlayerId);
public record BattleResponse(bool Success, string Code, string Message, Guid BatchId,
    BattleSnapshot State, BattleEvent[] Events);
