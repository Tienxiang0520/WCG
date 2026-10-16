using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WcgWeb.Components.Common;
using WcgWeb.Models;
using WcgWeb.Models.Battle;
using WcgWeb.Services;

namespace WcgTests;

public class CardFaceTests
{
    private static readonly CardDatabase Cards = Load();
    private static CardDatabase Load()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb")));
        return new CardDatabase(env.Object);
    }
    sealed class MemoryStorage : IPlayerStorage
    {
        public object Gate { get; } = new();
        public Dictionary<string, string> Values { get; } = new();
        public string? Read(string key) => Values.GetValueOrDefault(key);
        public void Write(string key, string value, string? expected) => Values[key] = value;
    }
    private static async Task<string> Render(string language, Dictionary<string, object?> parameters)
    {
        var services = new ServiceCollection();
        var store = new PlayerProfileStore(new MemoryStorage());
        var l = new Localizer(store); l.Set(language);
        services.AddSingleton(store).AddSingleton(l);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            System.Net.WebUtility.HtmlDecode((await renderer.RenderComponentAsync<CardFace>(ParameterView.FromDictionary(parameters))).ToHtmlString()));
    }

    [Fact]
    public async Task FullFaceCarriesEveryPhysicalCardElement()
    {
        var html = await Render(Localizer.Chinese, new() { ["Card"] = Cards.GetCard("WCG-061") });
        Assert.Contains("data-will=\"order\"", html);
        Assert.Matches(new Regex("face-name.*銀色誓言衛", RegexOptions.Singleline), html);
        Assert.Matches(new Regex("face-cost\"[^>]*><b>2</b><small>費</small>"), html);
        Assert.Matches(new Regex("face-band\"><span>秩序</span><i>・</i><span>怪物</span>"), html);
        Assert.Single(Regex.Matches(html, "face-arrow right"));
        Assert.Contains("<p>右側相鄰的己方怪物具有聖盾。</p>", html); // arrow prefix becomes a gold triangle instead of text
        Assert.Matches(new Regex("<small>力量</small><b>1000</b><small>PP</small>"), html);
        Assert.Matches(new Regex("<small>傷害</small><b>1</b><small>DP</small>"), html);
        Assert.Matches(new Regex("face-foot\"><span>魂誓</span><span>WCG-061</span>"), html);
        Assert.Contains("face-compass", html);
    }

    [Fact]
    public async Task EnglishFaceIsFullyTranslatedAndSpellsHaveNoStatsFooter()
    {
        var spell = Cards.AllCards.First(c => c.IsSpell);
        var html = await Render(Localizer.English, new() { ["Card"] = spell });
        Assert.DoesNotMatch(new Regex(@"[\u4e00-\u9fff]"), Regex.Replace(html, "<[^>]+>", " "));
        Assert.DoesNotContain("face-stats", html);
        Assert.Contains("<small>Cost</small>", html);
        Assert.Contains("<span>Soul Oath</span>", html);
    }

    [Fact]
    public async Task BattleCardsRenderTheSameFaceForPreviewsAndReplays()
    {
        var card = new BattleCard(Guid.NewGuid(), "WCG-147", "聖堂仲裁護衛", "怪物", "秩序", 3, 1500, 1, "【箭頭：← ↑】測試文字", "", ["left", "up"]);
        var html = await Render(Localizer.Chinese, new() { ["Battle"] = card });
        Assert.Equal(2, Regex.Matches(html, "face-arrow (left|up)").Count);
        Assert.Contains("<b>3</b>", html);
        Assert.Contains("<p>測試文字</p>", html);
    }

    [Theory]
    [InlineData("WCG-061", "order")] [InlineData("WCG-005", "wrath")] [InlineData("WCG-021", "reason")]
    [InlineData("WCG-049", "vitality")] [InlineData("WCG-085", "abyss")] [InlineData("WCG-101", "neutral")]
    public async Task EachWillGetsItsOwnFrameOrnaments(string id, string will)
    {
        var html = await Render(Localizer.English, new() { ["Card"] = Cards.GetCard(id) });
        var theme = CardFaceThemes.For(will);
        Assert.Contains($"data-will=\"{will}\"", html);
        Assert.Contains($"<path d=\"{theme.Corner}\"", html);
        Assert.Contains(theme.Emblem, html);
        // Only Order keeps the physical card's four-point stars and compass.
        Assert.Equal(will == "order", html.Contains(CardFaceThemes.Star));
    }

    [Fact]
    public void ThemesAreDistinctAndTheShowcaseScriptUsesTheSameShapes()
    {
        Assert.Equal(6, CardFaceThemes.All.Count);
        Assert.Equal(6, CardFaceThemes.All.Values.Select(t => t.Emblem).Distinct().Count());
        Assert.Equal(6, CardFaceThemes.All.Values.Select(t => t.Corner).Distinct().Count());
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/wwwroot"));
        var js = File.ReadAllText(Path.Combine(root, "battle/battle-feedback.js"));
        var json = Regex.Match(js, @"const FACE_THEMES = (\{.*\});").Groups[1].Value;
        var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json)!;
        foreach (var (key, t) in CardFaceThemes.All)
            Assert.Equal(new[] { t.Corner, t.Spark, t.Gem, t.Emblem }, new[] { parsed[key]["corner"], parsed[key]["spark"], parsed[key]["gem"], parsed[key]["emblem"] });
        var css = File.ReadAllText(Path.Combine(root, "card-face.css"));
        foreach (var key in CardFaceThemes.All.Keys.Where(k => k != "order")) Assert.Contains($"article.wcg-face[data-will={key}]{{", css);
    }

    [Theory]
    [InlineData("zh-Hant")] [InlineData("en")]
    public async Task UpArrowSitsOnTheCardsTopEdgeWhileSideArrowsStayOnTheSides(string language)
    {
        // 聖堂仲裁護衛 has ← and ↑: ← stays on the art's outer side, ↑ moves from the art window to the card's top edge.
        var html = await Render(language, new() { ["Card"] = Cards.GetCard("WCG-147") });
        Assert.Matches(new Regex("<article class=\"wcg-face[^\"]*has-up"), html);
        var art = Regex.Match(html, "<div class=\"face-art\">.*?</div>", RegexOptions.Singleline).Value;
        Assert.Contains("face-arrow left", art);
        Assert.DoesNotContain("face-arrow up", art);
        Assert.Matches(new Regex("<span class=\"face-arrow up\"[^>]*></span>\\s*<header class=\"face-head\">"), html);
        var css = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/wwwroot/card-face.css")));
        Assert.Contains("article.wcg-face>.face-card>.face-arrow.up{top:", css);
        Assert.Contains("article.wcg-face.has-up .face-star.top", css);
        var js = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/wwwroot/battle/battle-feedback.js")));
        Assert.Contains("body.append(node('span', `face-arrow ${a}`)); outer.classList.add(`has-${a}`)", js);
    }
}
