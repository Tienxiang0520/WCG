using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WcgWeb.Models;

namespace WcgWeb.Services;

// Interface language. The game itself (engine, saves, replays, card data) always works with the canonical
// 繁體中文 identifiers; this layer only changes what is displayed. Keys are the zh-Hant source strings
// (gettext style), so 繁體中文 needs no table and every key has exactly one English entry in i18n/en.json.
public sealed class Localizer : IDisposable
{
    public const string Chinese = "zh-Hant", English = "en";
    public static readonly IReadOnlyList<(string Id, string Name)> Languages = [(Chinese, "繁體中文"), (English, "English")];
    private readonly PlayerProfileStore profile;
    private string? detected;
    public event Action? Changed;

    public Localizer(PlayerProfileStore profile)
    {
        this.profile = profile;
        profile.Changed += ProfileChanged;
        current = Language;
    }
    private void ProfileChanged() { if (current != Language) { current = Language; Changed?.Invoke(); } }
    // The server registers one Localizer per circuit (browser detection is per visitor) over the shared profile.
    public void Dispose() => profile.Changed -= ProfileChanged;
    private string current;

    public static string? Normalize(string? value) => value switch { Chinese => Chinese, English => English, _ => null };
    public string Language => Normalize(profile.Language) ?? detected ?? Chinese;
    public bool IsEnglish => Language == English;
    public bool HasSavedChoice => Normalize(profile.Language) != null;

    public void Set(string language)
    {
        if (Normalize(language) is not { } value) return;
        profile.SetLanguage(value);
        if (current != Language) { current = Language; Changed?.Invoke(); }
    }

    // Browser preference is only used while no choice is saved: English only when the browser clearly prefers it.
    public void Detect(IEnumerable<string>? browserLanguages)
    {
        if (HasSavedChoice || detected != null) return;
        detected = PickFromBrowser(browserLanguages);
        if (current != Language) { current = Language; Changed?.Invoke(); }
    }
    public static string PickFromBrowser(IEnumerable<string>? browserLanguages)
    {
        var first = browserLanguages?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim().ToLowerInvariant() ?? "";
        return first == "en" || first.StartsWith("en-", StringComparison.Ordinal) ? English : Chinese;
    }

    // ---- lookups --------------------------------------------------------------------------------
    // Fixed interface text: L["中文"].
    public string this[string zh] => IsEnglish ? LocalizationCatalog.Translate(zh) : zh;
    // Formatted interface text: L.F("第 {0} 回合", turn). Arguments that are canonical strings are translated too.
    public string F(string zhFormat, params object?[] args)
    {
        if (!IsEnglish) return string.Format(CultureInfo.InvariantCulture, zhFormat, args);
        var values = args.Select(a => a is string s ? LocalizationCatalog.Translate(s) : a).ToArray();
        return string.Format(CultureInfo.InvariantCulture, LocalizationCatalog.Translate(zhFormat), values);
    }
    // Text produced by the engine or services at run time (messages, logs, labels, choice titles, saved reasons).
    public string Text(string? zh) => zh == null ? "" : IsEnglish ? LocalizationCatalog.Translate(zh) : zh;

    public string CardName(CardDefinition? card) => card == null ? "" : IsEnglish ? LocalizationCatalog.CardName(card.Id, card.Name) : card.Name;
    public string CardName(string id, string zhName) => IsEnglish ? LocalizationCatalog.CardName(id, zhName) : zhName;
    public string CardText(CardDefinition? card) => card == null ? "" : IsEnglish ? LocalizationCatalog.CardText(card.Id, card.Text) : card.Text;
    public string CardText(string id, string zhText) => IsEnglish ? LocalizationCatalog.CardText(id, zhText) : zhText;
    public string CardName(WcgWeb.Models.Battle.BattleCard? card) => card == null ? "" : CardName(card.CardId, card.Name);
    public string CardText(WcgWeb.Models.Battle.BattleCard? card) => card == null ? "" : CardText(card.CardId, card.Text);
    // Text shown on card faces and detail panels: vanilla cards ("無。" / "None.") leave the box blank. Search keeps using CardText.
    public string FaceText(CardDefinition? card) => card == null || card.IsVanilla ? "" : CardText(card);
    public string FaceText(string id, string zhText) => CardDefinition.IsVanillaText(zhText) ? "" : Blank(CardText(id, zhText));
    public string FaceText(WcgWeb.Models.Battle.BattleCard? card) => card == null ? "" : FaceText(card.CardId, card.Text);
    private static string Blank(string text) => CardDefinition.IsVanillaText(text) ? "" : text;
    public string Will(string? will) => Text(will);
    public string CardType(string? type) => Text(type);
    // Official and ranked decks are translated by id while they keep their original name; player-named decks stay as typed.
    public string DeckName(Deck? deck) => deck == null ? "" : IsEnglish ? LocalizationCatalog.DeckName(deck.Id, deck.Name) : deck.Name;
    public string DeckDescription(Deck? deck) => deck == null ? "" : IsEnglish ? LocalizationCatalog.DeckDescription(deck.Id, deck.Description) : deck.Description;
}

public static partial class LocalizationCatalog
{
    private sealed record CardEntry(string Name, string Text, string? ZhText = null);
    private sealed record DeckEntry(string Name, string Description, string ZhName, string ZhDescription);
    private sealed record Template(Regex Pattern, string English, int Weight);

    private static readonly Lazy<Data> data = new(Load);
    private sealed class Data
    {
        public Dictionary<string, string> Ui = new(StringComparer.Ordinal);
        public Dictionary<string, CardEntry> Cards = new(StringComparer.Ordinal);
        public Dictionary<string, string> CardNamesByZh = new(StringComparer.Ordinal);
        public Dictionary<string, DeckEntry> Decks = new(StringComparer.Ordinal);
        public Dictionary<string, string> DeckTextByZh = new(StringComparer.Ordinal);
        public List<Template> Templates = [];
    }
    private static readonly ConcurrentDictionary<string, string> cache = new(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, string> Strings => data.Value.Ui;
    public static IEnumerable<string> CardIds => data.Value.Cards.Keys;

    private static string Resource(string name)
    {
        using var stream = typeof(LocalizationCatalog).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing localization resource {name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static Data Load()
    {
        var result = new Data();
        var ui = JsonSerializer.Deserialize<Dictionary<string, string>>(Resource("WcgWeb.I18n.en.json")) ?? [];
        foreach (var (key, value) in ui) result.Ui[key] = value;
        using (var cards = JsonDocument.Parse(Resource("WcgWeb.I18n.cards.en.json")))
            foreach (var card in cards.RootElement.EnumerateObject())
            {
                var entry = new CardEntry(card.Value.GetProperty("name").GetString() ?? "", card.Value.GetProperty("text").GetString() ?? "",
                    card.Value.TryGetProperty("zh_text", out var zhText) ? zhText.GetString() : null);
                result.Cards[card.Name] = entry;
                if (card.Value.TryGetProperty("zh", out var zh) && zh.GetString() is { Length: > 0 } zhName) result.CardNamesByZh[zhName] = entry.Name;
            }
        using (var decks = JsonDocument.Parse(Resource("WcgWeb.I18n.decks.en.json")))
            foreach (var deck in decks.RootElement.EnumerateObject())
                result.Decks[deck.Name] = new(deck.Value.GetProperty("name").GetString() ?? "", deck.Value.GetProperty("description").GetString() ?? "",
                    deck.Value.GetProperty("zhName").GetString() ?? "", deck.Value.GetProperty("zhDescription").GetString() ?? "");
        foreach (var deck in result.Decks.Values)
        {
            result.DeckTextByZh.TryAdd(deck.ZhName, deck.Name); result.DeckTextByZh.TryAdd(deck.ZhDescription, deck.Description);
            // Ranked records keep the short archetype title ("秩序・白銀長城（控制）" → "白銀長城").
            var zhShort = Between(deck.ZhName, "・", "（"); var enShort = Between(deck.Name, " · ", " (");
            if (zhShort != deck.ZhName && enShort != deck.Name) result.DeckTextByZh.TryAdd(zhShort, enShort);
        }
        // Keys with {0}, {1}… also match run-time text such as engine logs; longer fixed text wins.
        foreach (var (key, value) in result.Ui)
        {
            if (!Hole().IsMatch(key)) continue;
            var parts = Hole().Split(key);
            if (parts.All(p => p.Length == 0)) continue;
            var pattern = "^" + Hole().Replace(Regex.Escape(key).Replace("\\{", "{"), "(.*?)") + "$";
            result.Templates.Add(new(new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Singleline), value, parts.Sum(p => p.Length)));
        }
        result.Templates.Sort((a, b) => b.Weight.CompareTo(a.Weight));
        return result;
    }
    private static string Between(string text, string start, string end)
    {
        var s = text.IndexOf(start, StringComparison.Ordinal); if (s >= 0) text = text[(s + start.Length)..];
        var e = text.IndexOf(end, StringComparison.Ordinal); return e > 0 ? text[..e] : text;
    }
    [GeneratedRegex(@"\{(\d+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Hole();

    public static string CardName(string id, string zhName) => data.Value.Cards.TryGetValue(id, out var c) && c.Name != "" ? c.Name : Translate(zhName);
    // Cards whose targeting changed pin the translated source text; historic cards use their own wording.
    public static string CardText(string id, string zhText) => data.Value.Cards.TryGetValue(id, out var c) && c.Text != ""
        && (c.ZhText == null || c.ZhText == zhText) ? c.Text : Translate(zhText);
    public static string DeckName(string id, string name) =>
        data.Value.Decks.TryGetValue(id, out var d) && (name == d.ZhName || name == d.Name) ? d.Name : Translate(name);
    public static string DeckDescription(string id, string text) =>
        data.Value.Decks.TryGetValue(id, out var d) && (text == d.ZhDescription || text == d.Description) ? d.Description : Translate(text);

    // Exact text, a card name, a known template (arguments translated recursively), or a few joined pieces.
    // Anything else (player-entered names, numbers) is returned unchanged.
    public static string Translate(string zh) => Translate(zh, 0);
    private static string Translate(string zh, int depth)
    {
        if (string.IsNullOrEmpty(zh) || !HasHan(zh)) return zh;
        if (cache.TryGetValue(zh, out var hit)) return hit;
        var d = data.Value;
        string result;
        if (d.Ui.TryGetValue(zh, out var exact)) result = exact;
        else if (d.CardNamesByZh.TryGetValue(zh, out var card)) result = card;
        else if (d.DeckTextByZh.TryGetValue(zh, out var deckText)) result = deckText;
        else result = Compose(zh, depth) ?? zh;
        if (cache.Count < 20000) cache[zh] = result;
        return result;
    }
    private static readonly string[] Separators = ["\n", " · ", "；", "。", "，", "、", "：", " / ", "／"];
    private static string? Compose(string zh, int depth)
    {
        if (depth > 6) return null;
        var d = data.Value;
        var trimmed = zh.Trim();
        if (trimmed != zh && trimmed.Length > 0) { var inner = Translate(trimmed, depth + 1); return HasHan(inner) ? null : zh.Replace(trimmed, inner); }
        // Strict pass: every argument must translate. Lenient pass (top level only): keep untranslatable
        // arguments as-is, so player-entered names such as a custom deck title survive inside English text.
        foreach (var strict in depth == 0 ? [true, false] : new[] { true })
            foreach (var t in d.Templates)
            {
                var m = t.Pattern.Match(zh);
                if (!m.Success) continue;
                var args = new object[m.Groups.Count - 1];
                var ok = true;
                for (var i = 1; i < m.Groups.Count; i++)
                {
                    var value = Translate(m.Groups[i].Value, depth + 1);
                    if (strict && HasHan(value)) { ok = false; break; }
                    args[i - 1] = value;
                }
                if (!ok) continue;
                try { return string.Format(CultureInfo.InvariantCulture, t.English, args); } catch (FormatException) { }
            }
        // 【名稱】 brackets around card or player names.
        if (zh.StartsWith('【') && zh.EndsWith('】') && zh.Length > 2) { var inner = Translate(zh[1..^1], depth + 1); if (!HasHan(inner)) return inner; }
        foreach (var sep in Separators)
        {
            if (!zh.Contains(sep, StringComparison.Ordinal)) continue;
            var pieces = zh.Split(sep);
            var translated = pieces.Select(p => p.Length == 0 ? p : Translate(p, depth + 1)).ToArray();
            if (translated.Any(HasHan)) continue;
            var englishSep = sep switch { "；" => "; ", "。" => ". ", "，" => ", ", "、" => ", ", "：" => ": ", "／" => " / ", _ => sep };
            return string.Join(englishSep, translated).Replace(" .", ".").TrimEnd();
        }
        return null;
    }
    public static bool HasHan(string? text)
    {
        if (text == null) return false;
        foreach (var ch in text) if (ch is >= '\u3400' and <= '\u9fff' or >= '\uf900' and <= '\ufaff') return true;
        return false;
    }
}
