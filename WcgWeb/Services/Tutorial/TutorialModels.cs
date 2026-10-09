namespace WcgWeb.Services.Tutorial;

// A fixed board used to start a tutorial lesson. Card ids only; the engine builds real card instances.
public sealed record TutorialUnit(string CardId, int Slot, bool Tapped = false, bool Set = false);

public sealed record TutorialSide
{
    public int Hp { get; init; } = 7;
    public string[] Hand { get; init; } = [];
    public string[] Deck { get; init; } = [];
    public string[] Graveyard { get; init; } = [];
    public int Energy { get; init; }
    public int SpentEnergy { get; init; }
    public bool FilledEnergy { get; init; }
    public TutorialUnit[] Board { get; init; } = [];
}

public sealed record TutorialScene(TutorialSide Player, TutorialSide Computer, int Turn = 3, bool ComputerActive = false, int Seed = 20261009);

// One accepted player action. Card ids are matched against the instance the command names.
// Option "SLOT:*" accepts any slot; OptionCard matches a choice that names a card instance.
public sealed record TutorialAllow(string Type, string? Card = null, string? Target = null, bool Face = false,
    string? Option = null, string? OptionCard = null)
{
    public static TutorialAllow Energy(string? card = null) => new("energy", card);
    public static TutorialAllow Play(string card, string? target = null) => new("play", card, target);
    public static TutorialAllow Set(string card) => new("set", card);
    public static TutorialAllow Activate(string card) => new("activate", card);
    public static TutorialAllow Attack(string card, string? target = null) => new("attack", card, target, Face: target == null);
    public static TutorialAllow Pick(string? target = null) => new("target", Target: target);
    public static TutorialAllow Slot(int? slot = null) => new("choice", Option: slot is { } s ? $"SLOT:{s}" : "SLOT:*");
    public static TutorialAllow Choose(string option) => new("choice", Option: option);
    public static TutorialAllow ChooseCard(string card) => new("choice", OptionCard: card);
    public static TutorialAllow AnyCardChoice() => new("choice", OptionCard: "*");
    public static TutorialAllow End() => new("end");
}

// A step without Done waits for 「下一步」. A step with Done but no Allow waits for the computer.
public sealed class TutorialStep
{
    public required string Title { get; init; }
    public required string Text { get; init; }
    public TutorialAllow[] Allow { get; init; } = [];
    public Func<GameEngine, bool>? Done { get; init; }
    public string[] Spot { get; init; } = [];
    public string Hint { get; init; } = "";
    public bool IsInfo => Done == null;
    public bool IsWaiting => Done != null && Allow.Length == 0;
}

// The computer's scripted moves during its own turn. Anything not scripted (its effect choices) uses the normal practice AI.
public sealed record TutorialComputerMove(string Type, string? Card = null, string? Target = null);

public sealed record TourStep(string Title, string Text, string? Selector = null, string? Check = null);

public sealed class TutorialLesson
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Summary { get; init; }
    public required string Icon { get; init; }
    public required string[] Topics { get; init; }
    public bool Beginner { get; init; }
    public TutorialScene? Scene { get; init; }
    public TutorialStep[] Steps { get; init; } = [];
    public TutorialComputerMove[] ComputerMoves { get; init; } = [];
    // Guided tours run on a real page; Route is that page.
    public string? Route { get; init; }
    public TourStep[] Tour { get; init; } = [];
    public bool IsBattle => Scene != null;
    public int StepCount => IsBattle ? Steps.Length : Tour.Length;
    public string Href => IsBattle ? $"tutorial?lesson={Id}" : $"{Route}{(Route!.Contains('?') ? "&" : "?")}tour={Id}";
}
