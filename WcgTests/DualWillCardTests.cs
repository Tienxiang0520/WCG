using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WcgWeb.Components.Common;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;

// 雙色卡 (dual-will) data model and deck legality. The dual card here is a test fixture; the real dual cards are 214/217/218/222/224/228/233/235/239/240.
public class DualWillCardTests
{
    private static readonly CardDatabase Db = Load();
    private static CardDatabase Load()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb")));
        return new CardDatabase(env.Object);
    }
    // Fixture written in the JSON form the catalogue uses; the array is deliberately out of will order.
    private static readonly CardDefinition Dual = JsonSerializer.Deserialize<CardDefinition>(
        """{"id":"TEST-DUAL","name":"測試雙色卡","type":"怪物","will":["深淵","理智"],"total_cost":3,"pp":1500,"dp":1,"text":"無。"}""")!;
    private static CardDefinition? Get(string id) => id == Dual.Id ? Dual : Db.GetCard(id);
    private static IEnumerable<string> Mono(string will, int count) =>
        Db.AllCards.Where(c => c.Will == will && !c.IsDual).SelectMany(c => Enumerable.Repeat(c.Id, 4)).Take(count);
    private static Deck Make(string main, int dual, params (string Will, int Count)[] rest)
    {
        var ids = Enumerable.Repeat(Dual.Id, dual).ToList();
        foreach (var (will, count) in rest) ids.AddRange(Mono(will, count));
        return new Deck { MainWill = main, CardIds = ids };
    }

    private static readonly JsonSerializerOptions Raw = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public void WillReadsLegacyStringAndArrayInFixedOrder()
    {
        Assert.Equal(["理智", "深淵"], Dual.Wills);
        Assert.Equal("理智", Dual.Will);
        Assert.True(Dual.IsDual && Dual.HasWill("深淵") && Dual.HasWill("理智") && !Dual.HasWill("狂怒"));
        var single = JsonSerializer.Deserialize<CardDefinition>("""{"id":"X","will":"狂怒"}""")!;
        Assert.Equal(["狂怒"], single.Wills); Assert.False(single.IsDual);
        Assert.Contains("\"will\":\"狂怒\"", JsonSerializer.Serialize(single, Raw));
        Assert.Contains("\"will\":[\"理智\",\"深淵\"]", JsonSerializer.Serialize(Dual, Raw));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CardDefinition>("""{"will":["狂怒","理智","生機"]}"""));
    }

    [Fact]
    public void CatalogueHasExactlyTheTenDesignedDualCards()
    {
        Assert.Equal(["WCG-214", "WCG-217", "WCG-218", "WCG-222", "WCG-224", "WCG-228", "WCG-233", "WCG-235", "WCG-239", "WCG-240"], Db.AllCards.Where(c => c.IsDual).Select(c => c.Id));
        Assert.All(Db.AllCards.Where(c => !c.IsDual), c => Assert.Single(c.Wills));
        Assert.All(Db.PresetDecks, d => Assert.True(d.IsValid(Db.GetCard, out var e), e));
    }

    [Theory][InlineData("理智")][InlineData("深淵")]
    public void DualCardFitsWhenDeckChoseBothWillsInEitherOrder(string main)
    {
        var other = main == "理智" ? "深淵" : "理智";
        var d = Make(main, 4, (main, 20), (other, 16), ("中立", 10));
        Assert.True(d.IsValid(Get, out var error), error);
        Assert.Equal(4 + 20, d.GetColorCounts(Get).Main);
    }

    [Fact]
    public void DualCardAloneImpliesTheSecondWill()
    {
        var d = Make("理智", 4, ("理智", 26), ("中立", 20));
        Assert.True(d.IsValid(Get, out var error), error);
    }

    [Fact]
    public void DualCardIsRejectedWhenMainWillIsNotOneOfItsWills()
    {
        var d = Make("狂怒", 4, ("狂怒", 26), ("中立", 20));
        Assert.False(d.IsValid(Get, out var error));
        Assert.Contains("雙色卡【測試雙色卡】", error);
    }

    [Fact]
    public void DualCardWithADifferentSecondWillIsAThirdFaction()
    {
        var d = Make("理智", 4, ("理智", 22), ("生機", 4), ("中立", 20));
        Assert.False(d.IsValid(Get, out var error));
        Assert.Contains("兩個派系", error);
    }

    [Fact]
    public void SecondaryWillHasNoCardLimit()
    {
        var d = Make("理智", 4, ("理智", 4), ("深淵", 42));
        Assert.True(d.IsValid(Get, out var error), error);
        Assert.True(d.GetColorCounts(Get).OffColor > 12);
        var mono = new Deck { MainWill = "狂怒", CardIds = [.. Mono("狂怒", 10), .. Mono("理智", 40)] };
        Assert.True(new RankedDecks(Db, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/Data/ranked_decks.json"))).IsLegal(mono, out var rankedError), rankedError);
    }

    [Fact]
    public void BuilderFitRequiresBothChosenWills()
    {
        Assert.True(Deck.DualFits(Dual, "深淵", "理智"));
        Assert.True(Deck.DualFits(Dual, "理智", ""));
        Assert.False(Deck.DualFits(Dual, "理智", "生機"));
        Assert.False(Deck.DualFits(Dual, "狂怒", "理智"));
        Assert.True(Deck.DualFits(Db.GetCard("WCG-001")!, "狂怒", "-"));
    }

    [Fact]
    public void PrintPlanAndBattleSnapshotCarryBothWills()
    {
        var deck = Make("理智", 4, ("理智", 26), ("中立", 20));
        var face = new PrintedCard(Dual.Id, Dual.Name, Dual.Type, Dual.Will, 3, 1500, 1, "", null, Dual.Wills);
        Assert.Equal("理智／深淵", face.WillLabel);
        Assert.Equal(["理智", "深淵"], face.Face.Wills!);
        Assert.Null(new PrintedCard("WCG-001", "", "怪物", "狂怒", 2, 1, 1, "").Face.Wills);
        Assert.NotNull(deck);
    }

    sealed class MemoryStorage : IPlayerStorage
    {
        public object Gate { get; } = new();
        public Dictionary<string, string> Values { get; } = new();
        public string? Read(string key) => Values.GetValueOrDefault(key);
        public void Write(string key, string value, string? expected) => Values[key] = value;
    }

    [Fact]
    public async Task CardFaceDrawsSplitFrameWithBothWillMarks()
    {
        var services = new ServiceCollection();
        var store = new PlayerProfileStore(new MemoryStorage());
        var l = new Localizer(store); l.Set("zh");
        services.AddSingleton(store).AddSingleton(l);
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () => System.Net.WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<CardFace>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Card"] = Dual }))).ToHtmlString()));
        Assert.Contains("is-dual", html);
        Assert.Contains("data-will=\"reason\"", html);
        Assert.Contains("data-will2=\"abyss\"", html);
        Assert.Contains("class=\"face-dual\" data-will=\"abyss\"", html);
        Assert.Contains("理智<b>／</b>", html);
        var mono = await renderer.Dispatcher.InvokeAsync(async () => System.Net.WebUtility.HtmlDecode(
            (await renderer.RenderComponentAsync<CardFace>(ParameterView.FromDictionary(new Dictionary<string, object?> { ["Card"] = Db.GetCard("WCG-001") }))).ToHtmlString()));
        Assert.DoesNotContain("face-dual", mono); Assert.DoesNotContain("data-will2", mono);
    }
}
