using WcgWeb.Models;
using WcgWeb.Models.Ranked;
namespace WcgWeb.Services;

// Ranked journals record the computer's concrete move ("aic:<kind>") so replays and restores apply it directly
// instead of re-running the AI search. One AI step makes exactly one successful top-level engine call: its
// targets and choices are separate AI steps. While capturing, every top-level primitive (depth 1, inside the AI
// step) notes its arguments as positions before it runs; a step whose capture is not exactly one locatable
// call falls back to the legacy "ai" entry, which still re-runs the search on replay.
public partial class GameEngine
{
    public const string AiCallPrefix = "aic:";
    public static readonly string[] AiCallKinds = ["energy", "set", "summon", "enchant", "spell", "spellat", "spellatp", "activate", "attack", "target", "ptarget", "choice", "cancel", "end"];
    private List<RankedAction?>? _aiCalls;
    // The seat whose move is being captured: the computer for ranked journals, either seat for PvP lockstep (Pvp/).
    private PlayerState? _captureSeat;
    // The mutation depth a captured top-level call is noted at: 1 inside an AI step or ApplySeatCall, 0 when the
    // primitive itself is the command (a PvP player's own move).
    private int _captureDepth = 1;
    internal void BeginAiCapture() { _aiCalls = []; _captureSeat = null; _captureDepth = 1; }
    internal void BeginSeatCapture(PlayerState seat, int depth) { _aiCalls = []; _captureSeat = seat; _captureDepth = depth; }
    // The captured move, or null when the step must be journaled as legacy "ai".
    internal RankedAction? EndAiCapture()
    {
        var calls = _aiCalls; _aiCalls = null; _captureSeat = null; _captureDepth = 1;
        return calls is [{ } only] ? only : null;
    }
    private List<MonsterInstance> AiUnits() => Player.Board.Concat(Computer.Board).ToList();
    // Null when not capturing a top-level call; a "broken" marker (null entry) when the call cannot be located.
    private (bool On, RankedAction? Call) AiNote(string kind, PlayerState? p = null, CardInstance? card = null, MonsterInstance? unit = null,
        MonsterInstance? target = null, PlayerState? playerTarget = null, ChoiceOption? option = null)
    {
        if (_aiCalls == null || _mutationDepth != _captureDepth) return (false, null);
        var seat = _captureSeat ?? Computer;
        if (p != null && p != seat) return (true, null);
        int source = -1, side = -1, index = -1, choice = -1;
        if (card != null && (source = seat.Hand.IndexOf(card)) < 0) return (true, null);
        var units = unit != null || target != null ? AiUnits() : null;
        if (unit != null && (source = units!.IndexOf(unit)) < 0) return (true, null);
        if (target != null) { side = 0; if ((index = units!.IndexOf(target)) < 0) return (true, null); }
        if (playerTarget != null) side = playerTarget == Player ? 2 : playerTarget == Computer ? 3 : -9;
        if (side == -9) return (true, null);
        if (option != null && (choice = CurrentPendingChoice?.Options.FindIndex(o => o.Id == option.Id) ?? -1) < 0) return (true, null);
        return (true, new(AiCallPrefix + kind, source, side, index, choice, RankedAiVersion));
    }
    private bool AiTracked((bool On, RankedAction? Call) note, Func<bool> body)
    {
        var result = Change(body);
        if (note.On && result) _aiCalls?.Add(note.Call);
        return result;
    }
    // Applies one recorded computer move as a single published change, exactly like the AI step that made it.
    public bool ApplyAiCall(RankedAction a) => ApplySeatCall(Computer, a);
    // The same move format for either seat (PvP lockstep): hand positions are the acting seat's hand; unit
    // positions and hero sides (2 = Player, 3 = Computer) are absolute, so both clients decode it identically.
    internal bool ApplySeatCall(PlayerState seat, RankedAction a) => Change(() =>
    {
        if (seat != Player && seat != Computer) throw new InvalidDataException("電腦操作格式不正確。");
        if (!a.Type.StartsWith(AiCallPrefix, StringComparison.Ordinal)) throw new InvalidDataException("電腦操作格式不正確。");
        var units = AiUnits(); var hand = seat.Hand;
        CardInstance Card() => a.Source >= 0 && a.Source < hand.Count ? hand[a.Source] : throw new InvalidDataException("電腦操作位置無效。");
        MonsterInstance Unit(int i) => i >= 0 && i < units.Count ? units[i] : throw new InvalidDataException("電腦操作位置無效。");
        MonsterInstance? Target() => a.TargetSide == 0 ? Unit(a.Target) : a.TargetSide == -1 ? null : throw new InvalidDataException("電腦操作位置無效。");
        PlayerState Hero() => a.TargetSide == 2 ? Player : a.TargetSide == 3 ? Computer : throw new InvalidDataException("電腦操作位置無效。");
        return a.Type[AiCallPrefix.Length..] switch
        {
            "energy" => PlayEnergy(seat, Card()),
            "set" => SetCard(seat, Card()),
            "summon" => SummonMonster(seat, Card()),
            "enchant" => PlayEnchantment(seat, Card()),
            "spell" => CastSpell(seat, Card()),
            "spellat" => CastSpellAt(seat, Card(), Target() ?? throw new InvalidDataException("電腦操作位置無效。")),
            "spellatp" => CastSpellAtPlayer(seat, Card(), Hero()),
            "activate" => Activate(seat, Unit(a.Source)),
            "attack" => Attack(seat, Unit(a.Source), Target()),
            "target" => SelectTarget(Target() ?? throw new InvalidDataException("電腦操作位置無效。")),
            "ptarget" => SelectPlayerTarget(Hero()),
            "choice" => CurrentPendingChoice is { } c && a.Choice >= 0 && a.Choice < c.Options.Count ? SelectChoice(c.Options[a.Choice]) : throw new InvalidDataException("電腦操作位置無效。"),
            "cancel" => CancelTargetOrChoice(),
            "end" => EndTurn(),
            _ => throw new InvalidDataException("電腦操作格式不正確。")
        };
    });
}
