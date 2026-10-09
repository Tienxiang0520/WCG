using System.Text.Json;
using WcgWeb.Models.Ranked;
namespace WcgWeb.Services;

public sealed partial class RankedStore
{
    public string Path { get; }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly IPlayerStorage storage;
    private string? expected;
    private bool loaded;
    // True when Load converted a pre-tiered-AI save; the caller should Save to persist the reset.
    public bool ResetOnLoad { get; private set; }
    public RankedStore(IPlayerStorage storage, string path = "瀏覽器本機存檔")
    { this.storage = storage; Path = path; }
    public RankedProfile Load()
    {
        expected = storage.Read("ranked"); loaded = true; ResetOnLoad = false;
        if (expected is null) return new();
        var p = JsonSerializer.Deserialize<RankedProfile>(expected) ?? throw new InvalidDataException("天梯存檔為空。");
        if (p.Version is not (1 or RankedRules.ProfileVersion) || p.Stars < 0 || p.History == null || p.Season == null || p.Season.Length != 7 || !DateTime.TryParseExact(p.Season + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            throw new InvalidDataException("天梯存檔格式不正確，原資料已保留。");
        if (p.BestStars < 0 || p.SeasonBest < 0 || p.Wins < 0 || p.Losses < 0 || p.History.Any(h => h == null))
            throw new InvalidDataException("天梯成績格式不正確。");
        if (p.Match is { } m && (m.Tier is < 0 or > 5 || m.PlayerDeck == null || m.ComputerDeck == null || m.PlayerDeck.CardIds == null || m.ComputerDeck.CardIds == null || m.Actions == null ||
            m.Actions.Any(a => a == null || a.RulesVersion is < 1 or > GameEngine.RankedAiVersion || a.Type is not ("ai" or "activate" or "set" or "energy" or "play" or "attack" or "target" or "choice" or "end" or "surrender" or "cancel"))))
            throw new InvalidDataException("天梯對局格式不正確。");
        // Optional history: drop unusable entries instead of blocking ranked play.
        if (p.Records != null) p.Records = p.Records.Where(ValidRecord).TakeLast(RankedRules.MaxRecords).ToList();
        if (p.Version < RankedRules.ProfileVersion) { ResetForTieredAi(p); ResetOnLoad = true; }
        return p;
    }
    public void Save(RankedProfile p)
    {
        if (!loaded) { expected = storage.Read("ranked"); loaded = true; }
        var json = JsonSerializer.Serialize(p, Json);
        storage.Write("ranked", json, expected);
        expected = json;
    }
    // The tiered AI cannot replay journals recorded with the old AI. Keep the old season in History,
    // keep the all-time best, and restart the current season from 0 stars without the unfinished match.
    public static void ResetForTieredAi(RankedProfile p)
    {
        if (p.Season != "" && (p.Stars > 0 || p.Wins > 0 || p.Losses > 0 || p.Match != null))
            p.History.Add(new(p.Season + " 舊版電腦", p.Stars, Math.Max(p.SeasonBest, p.Stars), p.Wins, p.Losses));
        p.Stars = 0; p.SeasonBest = 0; p.Wins = 0; p.Losses = 0; p.Match = null; p.Result = null;
        p.Version = RankedRules.ProfileVersion;
    }
    static bool ValidRecord(RankedRecord? r)
    {
        if (r == null || r.Tier is < 0 or > 5 || r.PlayerDeck?.CardIds == null || r.ComputerDeck?.CardIds == null || r.Season == null || r.Actions == null || r.Turns < 0) return false;
        try { return RankedRecord.Decode(r.Actions).All(a => a.RulesVersion is >= 1 and <= GameEngine.RankedAiVersion); }
        catch (InvalidDataException) { return false; }
    }
    public static RankedProfile Copy(RankedProfile p) => JsonSerializer.Deserialize<RankedProfile>(JsonSerializer.Serialize(p))!;
}
