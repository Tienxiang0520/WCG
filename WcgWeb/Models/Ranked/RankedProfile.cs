namespace WcgWeb.Models.Ranked;

public sealed class RankedProfile
{
    // 2 = tiered ranked AI (GameEngine.RankedAiVersion 4). Version 1 saves are archived and reset on load.
    public int Version { get; set; } = RankedRules.ProfileVersion;
    public string Season { get; set; } = "";
    public int Stars { get; set; }
    public int BestStars { get; set; }
    public int SeasonBest { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public string SelectedDeck { get; set; } = "";
    public List<SeasonRecord> History { get; set; } = [];
    public RankedMatch? Match { get; set; }
    public RankedResult? Result { get; set; }
}
public record SeasonRecord(string Season, int EndingStars, int BestStars, int Wins, int Losses);
public record RankedResult(Guid MatchId, string Season, bool Won, int Before, int After, string Reason);
public sealed class RankedMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Season { get; set; } = "";
    public int Seed { get; set; }
    public int Tier { get; set; }
    public bool PlayerFirst { get; set; }
    public string Rules { get; set; } = "";
    public Deck PlayerDeck { get; set; } = new();
    public Deck ComputerDeck { get; set; } = new();
    public bool Settled { get; set; }
    public List<RankedAction> Actions { get; set; } = [];
}
// Positions are recorded before each action so regenerated instance IDs are never persisted as commands.
public record RankedAction(string Type, int Source = -1, int TargetSide = -1, int Target = -1, int Choice = -1, int RulesVersion = 1);
public static class RankedRules
{
    public const int ProfileVersion = 2;
    public static readonly string[] Tiers = ["青銅", "白銀", "黃金", "白金", "鑽石", "大師"];
    public static int Tier(int stars) => Math.Min(5, Math.Max(0, stars) / 15);
    public static string Label(int stars) => Tier(stars) == 5 ? $"大師 · {stars - 75} 星" : $"{Tiers[Tier(stars)]} {new[] { "III", "II", "I" }[(stars % 15) / 5]}";
    public static int Apply(int stars, bool won) => won ? stars + 1 : Tier(stars) == 0 ? stars : Math.Max(Tier(stars) * 15, stars - 1);
    public static int Reset(int stars) => Math.Max(0, Tier(stars) - 2) * 15;
    public static string Season(DateTimeOffset now) => now.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM");
    public static void Advance(RankedProfile profile, DateTimeOffset now)
    {
        var season = Season(now);
        if (profile.Season == "") { profile.Season = season; return; }
        if (profile.Match is { Settled: false }) return;
        while (string.CompareOrdinal(profile.Season, season) < 0)
        {
            profile.History.Add(new(profile.Season, profile.Stars, profile.SeasonBest, profile.Wins, profile.Losses));
            profile.Stars = Reset(profile.Stars); profile.SeasonBest = profile.Stars; profile.Wins = profile.Losses = 0;
            profile.Season = DateTime.ParseExact(profile.Season + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).AddMonths(1).ToString("yyyy-MM");
        }
    }
}
