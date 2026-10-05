namespace LcgWeb.Models;

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

public class MonsterInstance
{
    public Guid InstanceId { get; set; } = Guid.NewGuid();
    public CardDefinition Card { get; set; } = null!;
    public int CurrentPP { get; set; }
    public int CurrentDP { get; set; }
    public bool HasAttacked { get; set; }
    public bool HasSummoningSickness { get; set; }
    public bool IsSilenced { get; set; }
    public bool IsStealthed { get; set; }
    public int? FrozenUntilTurn { get; set; }
    public bool IsFrozen => FrozenUntilTurn.HasValue;
    public bool IsTaunt => !IsSilenced && Card.HasTaunt;
    public bool HasCharge => !IsSilenced && Card.HasCharge;
    public bool HasPoison => !IsSilenced && Card.HasPoison;
    public bool HasTrample => !IsSilenced && Card.HasTrample;
    public List<CardInstance> ShieldEnergies { get; set; } = [];
    public int ShieldCount => ShieldEnergies.Count;
    public string? ShieldOwnerId { get; set; }
    public bool HasShield => ShieldCount > 0;
    public CardInstance? SilenceSpell { get; set; }
    public string? SilenceOwnerId { get; set; }

    public MonsterInstance(CardDefinition card)
    {
        Card = card;
        CurrentPP = card.PP ?? 0;
        CurrentDP = card.DP ?? 1;
        HasSummoningSickness = true;
        IsStealthed = card.HasStealth;
    }

    public void ResetTurnState()
    {
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
}

public record GameActionResult(bool Success, string Code, string Message, Guid MatchId, long Revision);
