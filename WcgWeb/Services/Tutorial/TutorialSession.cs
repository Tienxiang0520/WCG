using Microsoft.Extensions.Logging;
using WcgWeb.Models;
using WcgWeb.Models.Battle;

namespace WcgWeb.Services.Tutorial;

// One running battle lesson. It owns a private practice engine, so lessons never touch the training room or ranked saves.
// Player commands pass the step check first and then run through the normal battle rules.
public sealed class TutorialSession : IDisposable
{
    public GameEngine Engine { get; }
    public BattleBridge Bridge { get; }
    public BattleCoordinator Coordinator { get; }
    public TutorialLesson? Lesson { get; private set; }
    public int StepIndex { get; private set; }
    public bool Completed { get; private set; }
    public string Notice { get; private set; } = "";
    public int Attempt { get; private set; }
    public event Action? Changed;
    private readonly System.Collections.Concurrent.ConcurrentQueue<TutorialComputerMove> computerMoves = new();
    private readonly object gate = new();

    public TutorialSession(CardDatabase cards, DeckService decks, ILogger<BattleCoordinator> logger, TimeProvider? clock = null)
    {
        Engine = GameEngine.CreateTraining(cards);
        Bridge = new BattleBridge(Engine, decks) { RankedSubmit = Submit, RankedAi = ComputerStep };
        Coordinator = new BattleCoordinator(Bridge, Engine, logger, clock);
    }

    public TutorialStep? Step => Lesson is { } l && StepIndex < l.Steps.Length ? l.Steps[StepIndex] : null;
    public bool Failed => !Completed && Engine.ReadConsistent(() => Engine.Player.HasLost);

    public BattleResponse Load(TutorialLesson lesson)
    {
        if (!lesson.IsBattle) throw new ArgumentException("這一課不是對戰課程。");
        lock (gate)
        {
            Lesson = lesson; StepIndex = 0; Completed = false; Notice = ""; Attempt++;
            computerMoves.Clear(); foreach (var move in lesson.ComputerMoves) computerMoves.Enqueue(move);
        }
        if (!Engine.LoadTutorialScene(lesson.Scene!)) throw new InvalidOperationException(Engine.LastError);
        Changed?.Invoke();
        return new(true, "sync", "", Guid.NewGuid(), Bridge.Snapshot(), []);
    }

    public BattleResponse Restart() => Load(Lesson ?? throw new InvalidOperationException("尚未載入課程。"));

    // Info steps advance with 「下一步」; the last step finishes the lesson.
    public void Next()
    {
        lock (gate)
        {
            if (Step is not { IsInfo: true } || Completed) return;
            Notice = ""; Advance();
        }
        Evaluate();
    }

    public void Evaluate()
    {
        bool changed = false;
        lock (gate)
        {
            while (Step is { Done: { } done } && Engine.ReadConsistent(() => done(Engine))) { Advance(); changed = true; }
        }
        if (changed) Changed?.Invoke();
    }

    private void Advance()
    {
        StepIndex++; Notice = "";
        if (Lesson != null && StepIndex >= Lesson.Steps.Length) { StepIndex = Lesson.Steps.Length - 1; Completed = true; }
    }

    private BattleResponse Reject(string message) => new(false, "tutorial", message, Guid.NewGuid(), Bridge.Snapshot(), []);

    private BattleResponse Submit(BattleCommand command)
    {
        if (command.Type == "surrender") return Bridge.SubmitCore(command);
        if (command.Type is "start" or "reset" || command.Type.StartsWith("training-")) return Reject("教學中不能使用這個功能。");
        string? problem;
        lock (gate) problem = Check(command);
        if (problem != null)
        {
            lock (gate) Notice = problem;
            Changed?.Invoke();
            return Reject(problem);
        }
        var response = Bridge.SubmitCore(command);
        if (response.Success) { lock (gate) Notice = ""; Evaluate(); }
        return response;
    }

    // Null means the command belongs to the current step.
    internal string? Check(BattleCommand command)
    {
        if (Completed) return "本課已完成，可以重玩或前往下一課。";
        if (Step is not { } step) return "課程尚未開始。";
        if (step.IsInfo) return "先閱讀說明，然後按「下一步」。";
        if (step.IsWaiting) return "請等待電腦行動。";
        if (command.Type == "cancel") return null;
        return Engine.ReadConsistent(() => step.Allow.Any(a => Matches(a, command))) ? null : string.IsNullOrEmpty(step.Hint) ? $"這一步請依照提示操作：{step.Title}。" : step.Hint;
    }

    private string? CardOf(Guid? id)
    {
        if (id is not { } value) return null;
        foreach (var p in new[] { Engine.Player, Engine.Computer })
        {
            if (p.Board.FirstOrDefault(m => m.InstanceId == value) is { } m) return m.Card.Id;
            if (p.Hand.Concat(p.Graveyard).Concat(p.Deck).FirstOrDefault(c => c.InstanceId == value) is { } c) return c.Card.Id;
        }
        return null;
    }

    private bool Matches(TutorialAllow allow, BattleCommand command)
    {
        if (allow.Type != command.Type) return false;
        switch (command.Type)
        {
            case "energy": case "set": case "activate":
                return allow.Card == null || CardOf(command.InstanceId) == allow.Card;
            case "play":
                if (allow.Card != null && CardOf(command.InstanceId) != allow.Card) return false;
                return command.TargetId == null || allow.Target == null || CardOf(command.TargetId) == allow.Target;
            case "attack":
                if (allow.Card != null && CardOf(command.InstanceId) != allow.Card) return false;
                return allow.Face ? command.TargetId == null : command.TargetId != null && (allow.Target == null || CardOf(command.TargetId) == allow.Target);
            case "target":
                return allow.Target == null || CardOf(command.TargetId) == allow.Target;
            case "choice":
                var option = command.OptionId ?? "";
                if (allow.Option is { } wanted)
                    return wanted.EndsWith('*') ? option.StartsWith(wanted[..^1], StringComparison.Ordinal) : option == wanted;
                if (allow.OptionCard is { } card)
                    return option != "SKIP" && Guid.TryParse(option, out var id) && (card == "*" || CardOf(id) == card);
                return true;
            case "end":
                return true;
            default:
                return false;
        }
    }

    // The computer follows the lesson script on its turn; effect choices it must answer use the normal practice AI.
    private BattleResponse ComputerStep()
    {
        var response = Engine.ReadConsistent(() =>
        {
            bool changed;
            if (Engine.IsOver || Engine.DecisionPlayerId != "computer") changed = false;
            else if (Engine.IsWaiting) changed = Engine.ExecuteAiStep();
            else
            {
                computerMoves.TryDequeue(out var move);
                changed = move switch
                {
                    { Type: "attack" } m when Engine.Computer.Field.FirstOrDefault(x => x.Card.Id == m.Card) is { } attacker =>
                        Engine.Attack(Engine.Computer, attacker, m.Target == null ? null : Engine.Player.Field.FirstOrDefault(x => x.Card.Id == m.Target)),
                    _ => false
                };
                if (!changed) changed = Engine.EndTurn();
            }
            return new BattleResponse(changed, changed ? "ok" : "idle", "", Guid.NewGuid(), Bridge.Snapshot(), changed ? Engine.PresentationEvents : []);
        });
        if (response.Success) Evaluate();
        return response;
    }

    public void Dispose() => Coordinator.Dispose();
}
