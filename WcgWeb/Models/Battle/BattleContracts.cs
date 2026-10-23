namespace WcgWeb.Models.Battle;

public record BattleCommand(Guid CommandId, Guid MatchId, long ExpectedRevision, string Type,
    Guid? InstanceId = null, Guid? TargetId = null, string? OptionId = null,
    string? PlayerDeckId = null, string? ComputerDeckId = null, bool PlayerFirst = true, TrainingEdit? Training = null);
public record TrainingEdit(string Side = "player", string? CardId = null, int Slot = 0,
    int Hp = 7, int TotalEnergy = 0, int AvailableEnergy = 0, string? Scene = null);
public record BattleCard(Guid InstanceId, string CardId, string Name, string Type, string Will,
    int Cost, int? PP, int? DP, string Text, string Art, string[]? Arrows = null)
{
    // Both wills of a dual-will card (fixed will order); null for single-will cards so snapshots/replays stay unchanged.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Wills { get; init; }
}
public record BattleHand(BattleCard Card, bool CanPlay, bool CanEnergy, string Problem,
    string Preparation, string EnergyProblem, string[] PlayTargets, string Warning = "", bool CanSet = false);
public record BattleTarget(string Id, string Label, GameEnginePreview? Preview);
public record GameEnginePreview(bool AttackerDies, bool DefenderDies, bool AttackerShieldBreaks,
    bool DefenderShieldBreaks, int PlayerDamage, int PlayerDamagePreventable = 0, int RageEnergy = 0, int WildEnergy = 0);
public record BattleMonster(BattleCard Card, int PP, int DP, string[] Status,
    bool CanAttack, string Problem, BattleTarget[] Targets, BattleCard? Attachment, int Slot = -1, bool Tapped = false, bool IsSet = false, bool CanActivate = false, BattleCard[]? Attachments = null)
{
    // PP/DP modifiers (display only). Omitted when there are none so older snapshots stay unchanged.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BattleStatPart[]? PpParts { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public BattleStatPart[]? DpParts { get; init; }
    // Locked by an attachment (誓言枷鎖): can't attack and won't untap. Shown greyed on the board.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Locked { get; init; }
}
// One line of a PP/DP breakdown: the card that caused it (CardId null for engine effects such as 戰鬥加成) and its amount.
public record BattleStatPart(string Label, string? CardId, int Amount);
public record BattleEnergy(Guid InstanceId, bool Tapped)
{
    // 幸運幣 face up in the energy zone: counts as energy this turn only. Omitted when false so older snapshots stay unchanged.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool Temporary { get; init; }
}
public record BattleSide(string Id, int Hp, int DeckCount, int HandCount, int AvailableEnergy,
    int TotalEnergy, BattleEnergy[] Energy, BattleMonster[] Field, BattleCard[] Graveyard);
public record BattleOption(string Id, string Title, string Subtitle, BattleCard? Card);
public record BattlePending(string Kind, string Title, string Description, bool CanCancel,
    BattleOption[] Options, string[] Targets, Guid? SourceInstanceId = null);
public record BattleEvent(Guid Id, long Revision, int Order, string Type, string Side,
    Guid? InstanceId, Guid? TargetId, int Amount, BattleCard? Card, string Label);
public record BattleSnapshot(Guid MatchId, long Revision, int Turn, string Phase, string DecisionPlayerId,
    bool IsOver, string Outcome, BattleSide Player, BattleSide Computer, BattleHand[] Hand,
    BattlePending? Pending, string[] Logs, BattleCard[] RevealedCards, string RevealTitle, string ActivePlayerId, bool DamageCounters = true);
public record BattleResponse(bool Success, string Code, string Message, Guid BatchId,
    BattleSnapshot State, BattleEvent[] Events);
