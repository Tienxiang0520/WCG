using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace WcgTests;
public class DeckCodeTests
{
    static readonly IWebHostEnvironment Env = MakeEnv();
    static readonly CardDatabase Db = new(Env);
    static IWebHostEnvironment MakeEnv()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb"));
        var env = new Mock<IWebHostEnvironment>(); env.Setup(x => x.ContentRootPath).Returns(root); return env.Object;
    }
    static List<string> Sorted(IEnumerable<string> ids) => ids.OrderBy(x => x, StringComparer.Ordinal).ToList();

    [Fact] public void RoundTripKeepsCardsWillAndName()
    {
        var deck = DeckService.Copy(Db.PresetDecks[0]); deck.Name = "我的狂怒牌組";
        var code = DeckCode.Encode(deck);
        Assert.StartsWith("WCGD1-", code);
        var r = DeckCode.Decode(code, out var err)!;
        Assert.Equal("", err); Assert.Equal(deck.MainWill, r.MainWill); Assert.Equal("我的狂怒牌組", r.Name);
        Assert.Equal(Sorted(deck.CardIds), Sorted(r.CardIds));
    }
    [Fact] public void AllOfficialDecksRoundTrip()
    {
        var refs = new RankedDecks(Db, Env).All.Select(e => e.Deck);
        var all = Db.PresetDecks.Concat(refs).ToList();
        Assert.True(all.Count >= 60, $"only {all.Count}");
        foreach (var d in all)
        {
            var code = DeckCode.Encode(d);
            Assert.True(code.Length < 200, code);
            var r = DeckCode.Decode(code, out _)!;
            Assert.Equal(Sorted(d.CardIds), Sorted(r.CardIds)); Assert.Equal(d.MainWill, r.MainWill);
        }
    }
    [Fact] public void ChecksumAndPrefixFailures()
    {
        var code = DeckCode.Encode(DeckService.Copy(Db.PresetDecks[1]));
        var flipped = code[..^3] + (code[^3] == 'A' ? 'B' : 'A') + code[^2..];
        Assert.Null(DeckCode.Decode(flipped, out var e1)); Assert.Equal(DeckCode.InvalidError, e1);
        Assert.Null(DeckCode.Decode(code[..^4], out var e2)); Assert.Equal(DeckCode.InvalidError, e2);
        Assert.Null(DeckCode.Decode("XYZ-" + code[6..], out var e3)); Assert.Equal(DeckCode.InvalidError, e3);
        Assert.Null(DeckCode.Decode("WCGD2-" + code[6..], out var e4)); Assert.Contains("較新", e4);
        Assert.Null(DeckCode.Decode("WCGD1-!!!", out var e5)); Assert.Equal(DeckCode.InvalidError, e5);
    }
    [Fact] public void WhitespaceIgnoredAndLengthCapped()
    {
        var code = DeckCode.Encode(DeckService.Copy(Db.PresetDecks[2]));
        var spaced = "  " + string.Join("\n ", code.Chunk(7).Select(c => new string(c))) + "\r\n";
        Assert.NotNull(DeckCode.Decode(spaced, out _));
        Assert.Null(DeckCode.Decode("WCGD1-" + new string('A', 5000), out var e)); Assert.Equal("牌組代碼過長。", e);
        Assert.Null(DeckCode.Decode("   ", out var e2)); Assert.Equal("請貼上牌組代碼。", e2);
    }
    [Fact] public void UnknownAndLegacyIds()
    {
        var deck = new Deck { MainWill = "狂怒", CardIds = new() { "WCG-999", "LCG-001", "WCG-001", "CUSTOM-X" } };
        var r = DeckCode.Decode(DeckCode.Encode(deck), out _)!;
        Assert.Equal(new[] { "CUSTOM-X", "WCG-001", "WCG-001", "WCG-999" }, Sorted(r.CardIds));
        Assert.Null(Db.GetCard("WCG-999")); Assert.Null(Db.GetCard("CUSTOM-X"));
        var d = new Deck { MainWill = r.MainWill, CardIds = r.CardIds };
        Assert.Contains(d.Validate(Db.GetCard).Errors, x => x.Contains("WCG-999"));
    }
    [Fact] public void NameIsTruncatedTo24Chars()
    {
        var deck = DeckService.Copy(Db.PresetDecks[0]); deck.Name = new string('龍', 40);
        Assert.Equal(24, DeckCode.Decode(DeckCode.Encode(deck), out _)!.Name.Length);
    }
}
