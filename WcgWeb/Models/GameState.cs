namespace WcgWeb.Models;

public enum TurnPhase
{
    NotStarted,
    MainPhase,
    EndingTurn,
    GameOver
}

public class CardInstance
{
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    public CardDefinition Card { get; set; } = null!;

    public bool IsTapped { get; set; }
    // 幸運幣 (WCG-263) placed face up in the energy zone: 1 energy for this turn only, put into the graveyard at the end of its owner's turn.
    public bool TemporaryEnergy { get; set; }

    public CardInstance(CardDefinition card)
    {
        Card = card;
    }
}

// One line of a PP/DP breakdown: the card that caused it (null for engine-wide effects) and a zh-Hant label key.
public record StatSource(CardDefinition? Source, string Label, int Amount);

public record AttachedSpell(CardInstance Card, string OwnerId, int? ExpireTurn = null);

public class MonsterInstance
{
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    public CardDefinition Card { get; set; } = null!;
    private int basePP;
    public int BasePP => basePP;
    public Func<int>? Power { get; set; }
    public int CurrentPP { get => Power?.Invoke() ?? basePP; set => basePP = value; }
    public int Slot { get; set; } = -1;
    public bool IsTapped { get; set; }
    public bool IsSet { get; set; }
    public bool IsUnit => Card.IsMonster && !IsSet;
    public bool ShieldQualified { get; set; }
    // 聖盾 used this turn (simplified rules); every monster refreshes at each turn change.
    public bool ShieldSpent { get; set; }
    // 聖盾指示物 (2026-10-17 catalog): counters on the card, the 聖盾 sources already counted, and whether it was silenced last check.
    public int ShieldCounters { get; set; }
    public HashSet<string> ShieldSources { get; set; } = [];
    public bool WasSilenced { get; set; }
    public int NextCombatBonus { get; set; }
    public int TurnBonus { get; set; }
    // Display only (stat breakdown): which cards gave TurnBonus / NextCombatBonus. Never read by rules or AI.
    public List<StatSource> TurnBonusSources { get; } = [];
    public List<StatSource> NextCombatSources { get; } = [];
    public int TriggersThisTurn { get; set; }
    public List<AttachedSpell> Attachments { get; set; } = [];
    public bool AttackLocked => Attachments.Any(a => a.Card.Card.Id == "WCG-142");
    public int CombatPowerBonus { get; set; }
    public int AttackDamageBonus { get; set; }
    private int baseDP;
    public int BaseDP => baseDP;
    // 重蹄碾壓 (WCG-337–345): DP 指示物 on the card (+1 DP each, removed when it leaves play — a new instance starts at 0),
    // DP from attachments (set by RefreshBoard) and 「本回合」 DP (cleared at every turn change).
    public int DpCounters { get; set; }
    public int DpBonus { get; set; }
    public int TurnDpBonus { get; set; }
    // 「本回合嘲諷擋不住它」 (340／345); cleared at every turn change.
    public bool TurnUnblockable { get; set; }
    public int CurrentDP { get => baseDP + DpCounters + DpBonus + TurnDpBonus + AttackDamageBonus; set => baseDP = value; }
    public bool HasAttacked { get; set; }
    // 奪魂傀儡 (WCG-328–336): control of an opponent's monster. OwnerId is the owning player while someone else controls it
    // (null = its controller owns it). Turned = temporary control: the card stays in its owner's row turned 180°, at Slot,
    // and goes back at the end of turn ControlUntil. SoulCounter = 335's permanent steal (moved into the controller's row).
    public string? OwnerId { get; set; }
    public bool Turned { get; set; }
    public int ControlUntil { get; set; } = -1;
    public bool SoulCounter { get; set; }
    // 血誓指示物 on 364 血誓祭壇 (血誓反擊). They sit on the structure and leave play with it; silence never touches them.
    public int BloodCounters { get; set; }
    // 腐蝕指示物 (腐沼蔓生, WCG-382–390): -300 PP each, stacking; removed by silence, dispel, or leaving play.
    public int CorrosionCounters { get; set; }
    // 怒濤連擊 (WCG-391–399), all reset at every turn change: attacks made this turn (397), times readied by its own text
    // (394／399 limits), 396's "can't attack the enemy player this turn", and whether this 398 has readied a monster this turn.
    public int SurgeAttacks { get; set; }
    public int SurgeReadies { get; set; }
    public bool SurgeNoFace { get; set; }
    public bool SurgeHornUsed { get; set; }
    public bool Stolen => OwnerId != null;
    // 堅守號角 (WCG-294): untapped by it but can't attack this turn; cleared when its owner's next turn starts.
    public bool AttackBanned { get; set; }
    // Summoned out of its owner's energy zone (伏能喚醒, WCG-301–309); 沉睡巨像 checks it.
    public bool FromEnergy { get; set; }
    public bool HasSummoningSickness { get; set; }
    public bool IsSilenced { get; set; }
    public bool IsStealthed { get; set; }
    // Taunt/trample granted by board conditions or attachments (new cards 215/218/220/224); set by RefreshBoard.
    public bool ExtraTaunt { get; set; }
    public bool ExtraTrample { get; set; }
    // Cards that were attached when the monster died (戰紋傳承者).
    public List<CardInstance> LostAttachments { get; set; } = [];
    public bool IsTaunt => !IsSilenced && Card.HasTaunt || ExtraTaunt;
    public bool HasCharge => !IsSilenced && Card.HasCharge || Attachments.Any(a => a.Card.Card.Id == "WCG-141");
    public bool HasPoison => !IsSilenced && Card.HasPoison;
    public bool HasTrample => !IsSilenced && Card.HasTrample || ExtraTrample;
    public List<CardInstance> ShieldEnergies { get; set; } = [];
    public int ShieldCount => ShieldEnergies.Count;
    public string? ShieldOwnerId { get; set; }
    public bool HasShield => ShieldQualified;
    public bool HasRage => IsUnit && !IsSilenced && Card.HasRage;
    public bool HasObservation => IsUnit && !IsSilenced && Card.HasObservation;
    public bool HasWild => IsUnit && !IsSilenced && Card.HasWild;
    public bool HasSoulReturn => IsUnit && !IsSilenced && Card.HasSoulReturn;
    public bool HasHeroGuard => IsUnit && !IsSilenced && Card.HasHeroGuard;
    public CardInstance? SilenceSpell { get; set; }
    public string? SilenceOwnerId { get; set; }

    public MonsterInstance(CardDefinition card)
    {
        Card = card;
        CurrentPP = card.PP ?? 0;
        CurrentDP = card.DP ?? 1;
        HasSummoningSickness = true;
        IsStealthed = false;
        IsTapped = !card.HasCharge;
    }

    public void ResetTurnState()
    {
        if (!AttackLocked) IsTapped = false;
        TriggersThisTurn = 0;
        HasAttacked = false;
        HasSummoningSickness = false;
        AttackBanned = false;
    }
}

public class PlayerState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsAi { get; set; }
    public int Hp { get; set; } = 7;
    public const int MaxHp = 7;
    // The first player starts one life short (6 of 7) to offset the tempo of acting first; healing still caps at MaxHp.
    public const int FirstPlayerStartHp = 6;

    public List<CardInstance> Deck { get; set; } = new();
    public List<CardInstance> Hand { get; set; } = new();
    public List<CardInstance> EnergyZone { get; set; } = new();
    public List<MonsterInstance> Field { get; set; } = new();
    public List<MonsterInstance> Structures { get; set; } = new();
    public IEnumerable<MonsterInstance> Board => Field.Concat(Structures);
    // Cards physically in this row: turned monsters stay in their owner's row (Lent counts this player's monsters the
    // opponent controls in place; set by the engine's RefreshBoard).
    public int Lent { get; set; }
    public int Occupied => Field.Count(m => !m.Turned) + Structures.Count + Lent;
    public int NextCreatureDiscount { get; set; }
    // 潮汐輪迴 (WCG-373–381), all for the turn number stored: 374's next-paid-monster -1, 381's every-paid-monster -2,
    // and the turn you last returned one of your monsters to your hand (379 costs 2 less that turn).
    public int TideDiscountTurn { get; set; } = -1;
    public int TideSurgeTurn { get; set; } = -1;
    public int TideBounceTurn { get; set; } = -1;
    public List<CardInstance> Graveyard { get; set; } = new();

    public bool HasLost { get; set; }
    public string LossReason { get; set; } = "";

    public bool HasFilledEnergyThisTurn { get; set; }
    // 伏兵號令 (WCG-309): the turn number it was cast; energy summons that turn enter ready with +300.
    public int EnergyAmbushTurn { get; set; } = -1;
    // 327 千路齊進: the turn it was cast (monsters facing an empty slot get +1 DP attacking the player).
    public int LaneRushTurn { get; set; } = -1;
    public Dictionary<Guid, int> BurnedEnergy { get; set; } = []; // energy card → turn it was burned (WCG-314)
    public int AvailableEnergy => EnergyZone.Count(e => !e.IsTapped);
    public int AttachedEnergy => Field.Where(m => m.ShieldOwnerId == Id).Sum(m => m.ShieldCount);
    public int TotalEnergy => EnergyZone.Count + AttachedEnergy;
    public int TemporaryEnergy => EnergyZone.Count(e => e.TemporaryEnergy);

    public void ResetEnergyForOwnTurn()
    {
        HasFilledEnergyThisTurn = false;
        foreach (var energy in EnergyZone) energy.IsTapped = false;
    }

    // Resource counts never reveal the face-down card's faction.
    public Dictionary<string, int> GetEnergyCounts() => new() { ["通用"] = AvailableEnergy };

}

public class GameLogEntry
{
    public long ActionNumber { get; set; }
    public DateTime Time { get; set; } = DateTime.Now;
    public DateTime Timestamp => Time;
    public string Message { get; set; } = "";
    public string Level { get; set; } = "info"; // info, action, combat, danger, victory

    public GameLogEntry(string message, string level = "info")
    {
        Message = message;
        Level = level;
    }
}

public enum TargetRequirement
{
    None,
    FriendlyMonster,
    EnemyMonster,
    AnyMonster
}

public class ChoiceOption
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Icon { get; set; } = "✨";
    public TargetRequirement TargetType { get; set; } = TargetRequirement.None;
    public CardDefinition? PreviewCard { get; set; }
}

public class PendingChoice
{
    public Guid? SourceInstanceId { get; set; }
    public string OwnerId { get; set; } = "";
    public bool CanCancel { get; set; }
    public CardDefinition SourceCard { get; set; } = null!;
    public MonsterInstance? SourceMonster { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ChoiceOption> Options { get; set; } = new();
    public Action<ChoiceOption>? OnChoiceSelected { get; set; }
}

public class PendingTarget
{
    public Guid? SourceInstanceId { get; set; }
    public string OwnerId { get; set; } = "";
    public bool CanCancel { get; set; }
    public string ActionId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public TargetRequirement TargetType { get; set; } = TargetRequirement.AnyMonster;
    public Func<MonsterInstance, bool>? Validator { get; set; }
    public Action<MonsterInstance> OnTargetSelected { get; set; } = null!;
    public Func<PlayerState, bool>? PlayerValidator { get; set; }
    public Action<PlayerState>? OnPlayerTargetSelected { get; set; }
}

public record GameActionResult(bool Success, string Code, string Message, Guid MatchId, long Revision);
