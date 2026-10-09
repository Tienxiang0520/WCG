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
        Assert.Contains("<p>右側相鄰格的己方怪物具有聖盾。</p>", html); // arrow prefix becomes a gold triangle instead of text
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
}
