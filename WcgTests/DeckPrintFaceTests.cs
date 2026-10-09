using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Moq;
using WcgWeb.Components.Common;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;

// The physical print sheet must use the same CardFace (per-will frame theme, ornaments, arrow triangles) as the game.
public class DeckPrintFaceTests
{
    private readonly CardDatabase database = new(
        File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/Data/cards.json"))),
        File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/Data/preset_decks.json"))));
    sealed class MemoryStorage : IPlayerStorage
    {
        public object Gate { get; } = new();
        public Dictionary<string, string> Values { get; } = new();
        public string? Read(string key) => Values.GetValueOrDefault(key);
        public void Write(string key, string value, string? expected) => Values[key] = value;
    }
    // One card of every will (incl. Neutral) plus the arrow cards; the plan itself is never validated here.
    private static readonly string[] Mixed = ["WCG-001", "WCG-005", "WCG-021", "WCG-033", "WCG-049", "WCG-061", "WCG-085", "WCG-147", "WCG-164"];
    private DeckPrintPlan MixedPlan()
    {
        var cards = Mixed.Select(id => database.GetCard(id)!)
            .Select(c => new PrintedCard(c.Id, c.Name, c.Type, c.Will, c.TotalCost, c.PP, c.DP, c.Text, c.Arrows.ToArray())).ToArray();
        return new("混色", "狂怒", cards);
    }
    private static async Task<string> Render(string language, DeckPrintPlan plan)
    {
        var services = new ServiceCollection();
        var store = new PlayerProfileStore(new MemoryStorage());
        var l = new Localizer(store); l.Set(language);
        services.AddSingleton(store).AddSingleton(l).AddSingleton(Mock.Of<IJSRuntime>());
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () => System.Net.WebUtility.HtmlDecode((await renderer.RenderComponentAsync<DeckPrintPreview>(
            ParameterView.FromDictionary(new Dictionary<string, object?> { ["Plan"] = plan }))).ToHtmlString()));
    }
    private static string Cell(string html, string id) =>
        Regex.Match(html, $"<div class=\"print-card print-face-cell\" data-card-id=\"{id}\">.*?<svg class=\"crop-marks\"", RegexOptions.Singleline).Value;

    [Theory]
    [InlineData("zh-Hant")] [InlineData("en")]
    public async Task ColourPrintUsesTheCardFaceWithEachWillsFrameArrowsAndPrintArt(string language)
    {
        var html = await Render(language, MixedPlan());
        Assert.DoesNotContain("print-card-art", html); // the old print layout is gone
        Assert.Equal(Mixed.Length, Regex.Matches(html, "class=\"print-card print-face-cell\"").Count);
        Assert.Equal(Mixed.Length + 2, Regex.Matches(html, "class=\"crop-marks\"").Count); // + 2 life counter cards
        foreach (var id in Mixed)
        {
            var card = database.GetCard(id)!;
            var key = CardFaceThemes.Key(card.Will);
            var theme = CardFaceThemes.For(key);
            var cell = Cell(html, id);
            Assert.Matches(new Regex($"<article class=\"wcg-face [^\"]*print-face\" data-will=\"{key}\" data-card-id=\"{id}\""), cell);
            Assert.Contains($"src=\"print-art/{id}.jpg\"", cell);
            Assert.DoesNotContain("card-art/", cell.Replace("print-art/", ""));
            Assert.Contains(theme.Corner, cell); Assert.Contains(theme.Emblem, cell);
            Assert.Equal(card.Arrows.Length, Regex.Matches(cell, "class=\"face-arrow ").Count);
            foreach (var arrow in card.Arrows) Assert.Contains($"face-arrow {arrow}", cell);
            Assert.DoesNotContain("【箭頭", cell); Assert.DoesNotContain("[Arrow", cell);
        }
        Assert.Equal(new[] { "wrath", "reason", "vitality", "order", "abyss", "neutral" }.Order(),
            Regex.Matches(html, "data-will=\"(\\w+)\"").Select(m => m.Groups[1].Value).Distinct().Order());
        if (language == "en")
        {
            Assert.Contains("Raiding Wolf Rider", Cell(html, "WCG-005"));
            Assert.Contains("cut along the corner crop marks", html);
            Assert.DoesNotMatch(new Regex("face-band\"><span>[\\u4e00-\\u9fff]"), html);
        }
        else Assert.Contains("沿四角裁切線裁切", html);
    }

    [Fact]
    public void PrintPlanSnapshotsArrowsFromTheCardData()
    {
        var plan = DeckPrintPlan.Create(database.PresetDecks[0], database);
        var wolf = plan.Cards.First(c => c.Id == "WCG-005");
        Assert.Equal(["left", "right"], wolf.ArrowList);
        Assert.Equal(["left", "right"], wolf.Face.Arrows!);
        Assert.Equal("print-art/WCG-005.jpg", wolf.PrintArt);
        Assert.All(plan.Cards.Where(c => c.Id != "WCG-005"), c => Assert.Equal(database.GetCard(c.Id)!.Arrows, c.ArrowList));
    }
}
