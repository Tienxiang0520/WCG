using System.Text.Json;
using System.Text.Json.Serialization;

namespace WcgWeb.Models;

public sealed record ThemeDefinition(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("frame")] string Frame,
    [property: JsonPropertyName("legacy")] string? Legacy = null,
    [property: JsonPropertyName("minCards")] int MinCards = 30,
    [property: JsonPropertyName("maxCards")] int MaxCards = 50);

// Extensible registry of playable deck themes loaded from Data/themes.json.
// Visual frames stay decoupled from theme membership; legacy 5-will names resolve automatically for old saves/replays.
public static class ThemeCatalog
{
    public const string Neutral = "中立";

    private static readonly Lazy<IReadOnlyList<ThemeDefinition>> _all = new(Load);
    private static readonly Lazy<string[]> _names = new(() => All.Select(t => t.Id).ToArray());
    private static readonly Lazy<Dictionary<string, ThemeDefinition>> _byAnyName = new(BuildLookup);

    public static IReadOnlyList<ThemeDefinition> All => _all.Value;
    public static IReadOnlyList<string> Names => _names.Value;

    public static string Normalize(string? value)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed is "" or Neutral) return trimmed;
        return _byAnyName.Value.TryGetValue(trimmed, out var def) ? def.Id : trimmed;
    }

    public static bool IsTheme(string? value)
    {
        var norm = Normalize(value);
        return norm != "" && norm != Neutral && _byAnyName.Value.ContainsKey(norm);
    }

    public static int IndexOf(string? value)
    {
        var norm = Normalize(value);
        for (int i = 0; i < All.Count; i++)
            if (All[i].Id == norm) return i;
        return -1;
    }

    public static string FrameKey(string? value)
    {
        var norm = Normalize(value);
        return _byAnyName.Value.TryGetValue(norm, out var def) ? def.Frame : "neutral";
    }

    private static IReadOnlyList<ThemeDefinition> Load()
    {
        using var stream = typeof(ThemeCatalog).Assembly.GetManifestResourceStream("WcgWeb.Data.themes.json")
            ?? throw new InvalidOperationException("Missing embedded resource WcgWeb.Data.themes.json.");
        var items = JsonSerializer.Deserialize<List<ThemeDefinition>>(stream) ?? [];
        if (items.Count == 0) throw new InvalidOperationException("Theme catalog cannot be empty.");
        return items.AsReadOnly();
    }

    private static Dictionary<string, ThemeDefinition> BuildLookup()
    {
        var map = new Dictionary<string, ThemeDefinition>(StringComparer.Ordinal);
        foreach (var theme in All)
        {
            map[theme.Id] = theme;
            if (!string.IsNullOrWhiteSpace(theme.Legacy))
                map[theme.Legacy!] = theme;
        }
        return map;
    }
}
