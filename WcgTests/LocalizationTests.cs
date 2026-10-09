using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Models.Ranked;
using WcgWeb.Services;
using WcgWeb.Services.Tutorial;
namespace WcgTests;

// English mode must never fall back to 繁體中文: every UI key, card, deck, tutorial text and engine message
// that can reach the screen has to translate to text without Han characters.
public sealed class LocalizationTests
{
    static readonly string Repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
    readonly CardDatabase cards;
    public LocalizationTests()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(Path.Combine(Repo, "WcgWeb"));
        cards = new CardDatabase(env.Object);
    }
    static bool Han(string s) => LocalizationCatalog.HasHan(s);
    static void AllEnglish(IEnumerable<string> zh, string what)
    {
        var missing = zh.Where(Han).Distinct().Where(s => Han(LocalizationCatalog.Translate(s))).OrderBy(s => s, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, $"{what}: {missing.Count} untranslated\n" + string.Join("\n", missing.Take(60).Select(s => $"  {s} => {LocalizationCatalog.Translate(s)}")));
    }

    [Fact] public void DictionaryValuesAreEnglishAndKeepPlaceholders()
    {
        Assert.True(LocalizationCatalog.Strings.Count > 1000);
        foreach (var (zh, en) in LocalizationCatalog.Strings)
        {
            // 「第 n / m 步」 and 「再 n 星」 are split around a number; English drops these filler words.
            if (zh is "再" or "步") { Assert.Equal("", en); continue; }
            Assert.False(string.IsNullOrWhiteSpace(en), zh);
            Assert.False(Han(en), $"{zh} => {en}");
            var holes = new Func<string, string[]>(s => Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Order().ToArray());
            Assert.Equal(holes(zh), holes(en));
        }
    }

    [Fact] public void EveryCardAndDeckHasEnglishNameAndText()
    {
        foreach (var card in cards.AllCards)
        {
            Assert.False(Han(LocalizationCatalog.CardName(card.Id, card.Name)), card.Id + " name");
            Assert.False(Han(LocalizationCatalog.CardText(card.Id, card.Text)), card.Id + " text");
        }
        AllEnglish(cards.AllCards.SelectMany(c => new[] { c.Type.ToString(), c.Will.ToString() }), "card types/wills");
        foreach (var deck in cards.PresetDecks)
        {
            Assert.False(Han(LocalizationCatalog.DeckName(deck.Id, deck.Name)), deck.Id);
            Assert.False(Han(LocalizationCatalog.DeckDescription(deck.Id, deck.Description ?? "")), deck.Id + " description");
        }
    }

    // Every literal handed to L[...], L.F(...) or L.Text(...) in the components that ship in both builds.
    [Fact] public void EveryRazorLiteralIsTranslated()
    {
        var files = new[] { "WcgWeb/Components", "SoulOath.Static/Components" }
            .SelectMany(d => Directory.EnumerateFiles(Path.Combine(Repo, d), "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.EndsWith("Home.razor") && !f.EndsWith("BattleLab.razor")).ToList();
        Assert.True(files.Count > 20);
        var call = new Regex(@"\bL(?:\[|\.F\(|\.Text\()");
        var literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
        var keys = new List<string>();
        foreach (var file in files)
            foreach (var line in File.ReadLines(file))
                foreach (System.Text.RegularExpressions.Match m in call.Matches(line))
                {
                    // Scan to the matching close bracket so ternaries like L[a ? "x" : "y"] are covered.
                    int depth = 0, i = m.Index + m.Length - 1; var open = line[i]; var close = open == '[' ? ']' : ')';
                    var start = i;
                    for (; i < line.Length; i++) { if (line[i] == open) depth++; else if (line[i] == close && --depth == 0) break; }
                    keys.AddRange(literal.Matches(line[start..Math.Min(i + 1, line.Length)]).Select(x => Regex.Unescape(x.Groups[1].Value)));
                }
        Assert.True(keys.Count > 500, keys.Count.ToString());
        AllEnglish(keys, "razor literals");
    }

    [Fact] public void TutorialLessonsAndToursAreTranslated()
    {
        var texts = TutorialLessons.All.SelectMany(l => new[] { l.Title, l.Summary }.Concat(l.Topics)
            .Concat(l.Steps.SelectMany(s => new[] { s.Title, s.Text, s.Hint }))
            .Concat(l.Tour.SelectMany(t => new[] { t.Title, t.Text })));
        Assert.Equal(14, TutorialLessons.All.Count);
        AllEnglish(texts, "tutorial");
    }

    [Fact] public void RankedLabelsAndStrategiesAreTranslated()
        => AllEnglish(Enumerable.Range(0, 120).Select(RankedRules.Label).Concat(Enumerable.Range(0, 6).Select(RankedDecks.Strategy)), "ranked");

    // Plays every preset matchup AI-vs-AI and checks each message the battle screen can show.
    [Fact] public void EngineMessagesFromFullGamesAreTranslated()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var env = new Mock<IWebHostEnvironment>(); env.Setup(x => x.ContentRootPath).Returns(Path.Combine(Repo, "WcgWeb"));
        var decks = new DeckService(cards, env.Object);
        foreach (var p in cards.PresetDecks) foreach (var e in cards.PresetDecks)
        {
            var g = new GameEngine(cards, new Random(41)); g.StartGame(p, e); g.Player.IsAi = true; g.Computer.IsAi = true;
            var bridge = new BattleBridge(g, decks);
            for (int i = 0; i < 3000 && !g.IsOver; i++)
            {
                if (!g.ExecuteAiStep()) break;
                Collect(bridge.Snapshot(), seen);
            }
            Collect(bridge.Snapshot(), seen);
        }
        // Animation banner labels (Present(..., label: "...")) are fixed literals in the engine source.
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repo, "WcgWeb/Services"), "GameEngine*.cs"))
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(File.ReadAllText(file), "label:\\s*\"([^\"]+)\""))
                seen.Add(m.Groups[1].Value);
        Assert.True(seen.Count > 100, seen.Count.ToString());
        AllEnglish(seen, "engine messages");
    }
    // Card names and texts are translated by card id, so they are skipped here.
    static readonly HashSet<string> CardFields = ["Name", "Text", "CardId", "Id", "MatchId", "DecisionPlayerId", "ActivePlayerId"];
    static void Collect(object? value, HashSet<string> into, string field = "")
    {
        switch (value)
        {
            case null: return;
            case string s: if (!CardFields.Contains(field)) into.Add(s); return;
            case IEnumerable items: foreach (var item in items) Collect(item, into, field); return;
        }
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is Guid) return;
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (prop.GetIndexParameters().Length == 0 && prop.Name != "EqualityContract") Collect(prop.GetValue(value), into, prop.Name);
    }

    [Theory]
    [InlineData(new string[0], "zh-Hant")]
    [InlineData(new[] { "en-US", "zh-TW" }, "en")]
    [InlineData(new[] { "en" }, "en")]
    [InlineData(new[] { "zh-TW", "en-US" }, "zh-Hant")]
    [InlineData(new[] { "ja-JP", "en" }, "zh-Hant")]
    [InlineData(new[] { "english" }, "zh-Hant")]
    public void BrowserDetectionOnlyPicksEnglishWhenClearlyPreferred(string[] languages, string expected)
        => Assert.Equal(expected, Localizer.PickFromBrowser(languages));

    sealed class MemoryStorage : IPlayerStorage
    {
        public object Gate { get; } = new();
        public Dictionary<string, string> Values { get; } = new();
        public string? Read(string key) => Values.GetValueOrDefault(key);
        public void Write(string key, string value, string? expected) { if (Values.GetValueOrDefault(key) != expected) throw new IOException("stale"); Values[key] = value; }
    }

    [Fact] public void LanguageIsAnOptionalProfileSettingThatSurvivesAvatarChanges()
    {
        var storage = new MemoryStorage();
        storage.Values["wcg.profile"] = "{\"Avatar\":\"builtin:wolf\"}";
        var key = storage.Values.Keys.Single();
        var store = new PlayerProfileStore(storage);
        if (store.Avatar != "builtin:wolf") { storage.Values.Clear(); store = new PlayerProfileStore(storage); store.SetAvatar("builtin:wolf"); key = storage.Values.Keys.Single(); }
        var l = new Localizer(store);
        Assert.Null(store.Language); Assert.False(l.HasSavedChoice); Assert.Equal("zh-Hant", l.Language);
        l.Detect(["en-GB"]); Assert.True(l.IsEnglish); Assert.Null(new PlayerProfileStore(storage).Language);
        var changes = 0; l.Changed += () => changes++;
        l.Set("zh-Hant"); Assert.False(l.IsEnglish); Assert.Equal(1, changes);
        l.Set("en"); store.SetAvatar("builtin:sage");
        var reloaded = new PlayerProfileStore(storage);
        Assert.Equal("en", reloaded.Language); Assert.Equal("builtin:sage", reloaded.Avatar);
        l.Set("fr"); Assert.Equal("en", new PlayerProfileStore(storage).Language);
        Assert.DoesNotContain("English", storage.Values[key]);
        // A saved choice wins over the browser.
        var again = new Localizer(new PlayerProfileStore(storage)); again.Detect(["zh-TW"]); Assert.True(again.IsEnglish);
    }

    [Fact] public void TranslationKeepsPlayerNamesAndComposesKnownPieces()
    {
        Assert.Equal("English", LocalizationCatalog.Translate("English"));
        var deck = cards.PresetDecks[0];
        var composed = LocalizationCatalog.Translate("儲存失敗：" + "備份過大。");
        Assert.False(Han(composed), composed);
        Assert.Contains("我的牌組", LocalizationCatalog.Translate("我的牌組"));
        Assert.False(Han(LocalizationCatalog.Translate(deck.Name)));
    }
}
