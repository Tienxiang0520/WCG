using System.Text.Json;
using LcgWeb.Models.Ranked;
namespace LcgWeb.Services;

public sealed partial class RankedStore
{
    public string Path { get; }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly IPlayerStorage storage;
    private string? expected;
    private bool loaded;
    public RankedStore(IPlayerStorage storage, string path = "瀏覽器本機存檔")
    { this.storage = storage; Path = path; }
    public RankedProfile Load()
    {
        expected = storage.Read("ranked"); loaded = true;
        if (expected is null) return new();
        var p = JsonSerializer.Deserialize<RankedProfile>(expected) ?? throw new InvalidDataException("天梯存檔為空。");
        if (p.Version != 1 || p.Stars < 0 || p.History == null || p.Season == null || p.Season.Length != 7 || !DateTime.TryParseExact(p.Season + "-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
            throw new InvalidDataException("天梯存檔格式不正確，原資料已保留。");
        if (p.BestStars < 0 || p.SeasonBest < 0 || p.Wins < 0 || p.Losses < 0 || p.History.Any(h => h == null))
            throw new InvalidDataException("天梯成績格式不正確。");
        if (p.Match is { } m && (m.Tier is < 0 or > 5 || m.PlayerDeck == null || m.ComputerDeck == null || m.PlayerDeck.CardIds == null || m.ComputerDeck.CardIds == null || m.Actions == null ||
            m.Actions.Any(a => a == null || a.RulesVersion is < 1 or > 2 || a.Type is not ("ai" or "energy" or "play" or "attack" or "target" or "choice" or "end" or "surrender" or "cancel"))))
            throw new InvalidDataException("天梯對局格式不正確。");
        return p;
    }
    public void Save(RankedProfile p)
    {
        if (!loaded) { expected = storage.Read("ranked"); loaded = true; }
        var json = JsonSerializer.Serialize(p, Json);
        storage.Write("ranked", json, expected);
        expected = json;
    }
    public static RankedProfile Copy(RankedProfile p) => JsonSerializer.Deserialize<RankedProfile>(JsonSerializer.Serialize(p))!;
}
