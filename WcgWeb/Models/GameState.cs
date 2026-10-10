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

    public CardInstance(CardDefinition card)
    {
        Card = card;
    }
}

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
    public int NextCombatBonus { get; set; }
    public int TurnBonus { get; set; }
    public int TriggersThisTurn { get; set; }
    public List<AttachedSpell> Attachments { get; set; } = [];
    public bool AttackLocked => Attachments.Any(a => a.Card.Card.Id == "WCG-142");
    public int CurrentDP { get; set; }
    public bool HasAttacked { get; set; }
    public bool HasSummoningSickness { get; set; }
    public bool IsSilenced { get; set; }
    public bool IsStealthed { get; set; }
    public int? FrozenUntilTurn { get; set; }
    public bool IsFrozen => FrozenUntilTurn.HasValue;
    public bool IsTaunt => !IsSilenced && Card.HasTaunt;
    public bool HasCharge => !IsSilenced && Card.HasCharge || Attachments.Any(a => a.Card.Card.Id == "WCG-141");
    public bool HasPoison => !IsSilenced && Card.HasPoison;
    public bool HasTrample => !IsSilenced && Card.HasTrample;
    public List<CardInstance> ShieldEnergies { get; set; } = [];
    public int ShieldCount => ShieldEnergies.Count;
    public string? ShieldOwnerId { get; set; }
    public bool HasShield => ShieldQualified;
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
    }
}

public class PlayerState
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsAi { get; set; }
    public int Hp { get; set; } = 7;
    public const int MaxHp = 7;

    public List<CardInstance> Deck { get; set; } = new();
    public List<CardInstance> Hand { get; set; } = new();
    public List<CardInstance> EnergyZone { get; set; } = new();
    public List<MonsterInstance> Field { get; set; } = new();
    public List<MonsterInstance> Structures { get; set; } = new();
    public IEnumerable<MonsterInstance> Board => Field.Concat(Structures);
    public int Occupied => Field.Count + Structures.Count;
    public int NextCreatureDiscount { get; set; }
    public List<CardInstance> Graveyard { get; set; } = new();

    public bool HasLost { get; set; }
    public string LossReason { get; set; } = "";

    public bool HasFilledEnergyThisTurn { get; set; }
    public int AvailableEnergy => EnergyZone.Count(e => !e.IsTapped);
    public int AttachedEnergy => Field.Where(m => m.ShieldOwnerId == Id).Sum(m => m.ShieldCount);
    public int TotalEnergy => EnergyZone.Count + AttachedEnergy;

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
