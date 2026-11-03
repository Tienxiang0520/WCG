using WcgWeb.Models;
using WcgWeb.Services;
using WcgWeb.Services.Pvp;

namespace WcgTests;

// 好友對戰 PR 2: copy-paste connection codes (PvpSignalCode) and the handshake rules (PvpProtocol).
public class PvpSignalCodeTests
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WcgWeb", "Data");
    private static readonly CardDatabase Cards = new(File.ReadAllText(Path.Combine(Data, "cards.json")), File.ReadAllText(Path.Combine(Data, "preset_decks.json")));

    // A Chrome data-channel offer after ICE gathering (fingerprint and ufrag from a real Chrome 156 offer).
    private const string ChromeOffer =
        "v=0\r\no=- 1371054415236533011 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0\r\na=extmap-allow-mixed\r\na=msid-semantic: WMS\r\n" +
        "m=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\nc=IN IP4 0.0.0.0\r\n" +
        "a=candidate:3651413512 1 udp 2113937151 0b9e2f4c-7a51-4c3e-9b1d-4f2a3c5d6e7f.local 54012 typ host generation 0 network-cost 999\r\n" +
        "a=candidate:842163049 1 udp 1677729535 203.0.113.7 61234 typ srflx raddr 0.0.0.0 rport 0 generation 0 network-cost 999\r\n" +
        "a=candidate:1111 1 udp 2122260223 2001:db8::1 50000 typ host generation 0\r\n" +
        "a=candidate:2222 1 tcp 1518280447 192.168.1.5 9 typ host tcptype active generation 0\r\n" +
        "a=candidate:3333 2 udp 2122260222 192.168.1.5 50001 typ host\r\n" +
        "a=ice-ufrag:A53u\r\na=ice-pwd:xp9gvuw63HAWCJvNfz5uH/Ek\r\na=ice-options:trickle\r\n" +
        "a=fingerprint:sha-256 E1:B5:8C:A6:B9:EF:CF:F3:E7:BE:4F:E4:9C:41:C1:53:AA:C9:C9:30:83:3F:72:D2:99:D0:48:2E:EB:7A:B7:3F\r\n" +
        "a=setup:actpass\r\na=mid:0\r\na=sctp-port:5000\r\na=max-message-size:262144\r\n";

    private sealed class MemoryStorage : IPlayerStorage
    {
        public object Gate { get; } = new();
        private readonly Dictionary<string, string> values = new();
        public string? Read(string key) => values.GetValueOrDefault(key);
        public void Write(string key, string value, string? expected) => values[key] = value;
    }

    private static string OfferCode() => PvpSignalCode.Encode(PvpSignalCode.Parse(ChromeOffer, PvpSignalCode.Kind.Offer, out _)!);

    [Fact]
    public void OfferRoundTripsAndKeepsOnlyUdpComponentOneCandidates()
    {
        var s = PvpSignalCode.Parse(ChromeOffer, PvpSignalCode.Kind.Offer, out var err);
        Assert.Equal("", err); Assert.NotNull(s);
        Assert.Equal(3, s!.Candidates.Count); // tcp and component 2 dropped
        var code = PvpSignalCode.Encode(s);
        Assert.StartsWith("WCGP1-", code);
        Assert.True(code.Length < 200, $"code is {code.Length} chars");
        var back = PvpSignalCode.Decode(code, PvpSignalCode.Kind.Offer, out err);
        Assert.Equal("", err);
        Assert.Equal(s.Ufrag, back!.Ufrag); Assert.Equal(s.Pwd, back.Pwd); Assert.Equal(s.Fingerprint, back.Fingerprint); Assert.Equal(0, back.Setup);
        Assert.Equal(s.Candidates, back.Candidates);
        Assert.Equal("0b9e2f4c-7a51-4c3e-9b1d-4f2a3c5d6e7f.local", back.Candidates[0].Address);
        Assert.Equal("2001:db8::1", back.Candidates[2].Address);
    }

    [Fact]
    public void RebuiltSdpCarriesEverythingNeededAndParsesBackTheSame()
    {
        var s = PvpSignalCode.Decode(OfferCode(), PvpSignalCode.Kind.Offer, out _)!;
        var sdp = PvpSignalCode.Rebuild(s);
        foreach (var line in new[] { "m=application 9 UDP/DTLS/SCTP webrtc-datachannel", "a=ice-ufrag:A53u", "a=ice-pwd:xp9gvuw63HAWCJvNfz5uH/Ek", "a=setup:actpass", "a=mid:0", "a=sctp-port:5000",
                     "a=fingerprint:sha-256 E1:B5:8C:A6:B9:EF:CF:F3:E7:BE:4F:E4:9C:41:C1:53:AA:C9:C9:30:83:3F:72:D2:99:D0:48:2E:EB:7A:B7:3F",
                     "a=candidate:2 1 udp 1677729535 203.0.113.7 61234 typ srflx raddr 0.0.0.0 rport 0" })
            Assert.Contains(line + "\r\n", sdp);
        var again = PvpSignalCode.Parse(sdp, PvpSignalCode.Kind.Offer, out var err);
        Assert.Equal("", err);
        Assert.Equal(PvpSignalCode.Encode(s), PvpSignalCode.Encode(again!));
    }

    [Fact]
    public void WhitespaceAndLineBreaksAreIgnored()
    {
        var code = OfferCode();
        var messy = "  " + code[..20] + "\n\r\n " + code[20..50] + "\t" + code[50..] + "  ";
        Assert.NotNull(PvpSignalCode.Decode(messy, PvpSignalCode.Kind.Offer, out var err));
        Assert.Equal("", err);
    }

    [Fact]
    public void BadCodesGiveClearErrors()
    {
        var code = OfferCode();
        string Err(string? c, PvpSignalCode.Kind k = PvpSignalCode.Kind.Offer) { Assert.Null(PvpSignalCode.Decode(c, k, out var e)); return e; }
        Assert.Equal(PvpSignalCode.EmptyError, Err("  \n"));
        Assert.Equal(PvpSignalCode.EmptyError, Err(null));
        Assert.Equal(PvpSignalCode.InvalidError, Err(code[..^6]));                      // truncated
        Assert.Equal(PvpSignalCode.InvalidError, Err(code[..10] + (code[10] == 'A' ? 'B' : 'A') + code[11..])); // one character changed
        Assert.Equal(PvpSignalCode.InvalidError, Err("hello world"));
        Assert.Equal(PvpSignalCode.InvalidError, Err("WCGP1-!!!!"));
        Assert.Equal(PvpSignalCode.NewerError, Err("WCGP2-AAAA"));
        Assert.Equal("這是牌組代碼，不是連線代碼。", Err(DeckCode.Encode(new Deck { Name = "x", CardIds = ["WCG-001"] })));
        Assert.Equal(PvpSignalCode.ExpectAnswerError, Err(code, PvpSignalCode.Kind.Answer)); // host pasted its own room code
        var answer = PvpSignalCode.Encode(PvpSignalCode.Parse(ChromeOffer.Replace("a=setup:actpass", "a=setup:active"), PvpSignalCode.Kind.Answer, out _)!);
        Assert.Equal(PvpSignalCode.ExpectOfferError, Err(answer));                       // guest pasted a reply code
        Assert.Equal(1, PvpSignalCode.Decode(answer, PvpSignalCode.Kind.Answer, out _)!.Setup);
    }

    [Fact]
    public void SdpWithoutCandidatesOrKeysIsRejected()
    {
        var noCands = string.Join("\r\n", ChromeOffer.Split("\r\n").Where(l => !l.StartsWith("a=candidate")));
        Assert.Null(PvpSignalCode.Parse(noCands, PvpSignalCode.Kind.Offer, out var err));
        Assert.Equal(PvpSignalCode.NoCandidateError, err);
        Assert.Null(PvpSignalCode.Parse(ChromeOffer.Replace("a=fingerprint:sha-256", "a=fingerprint:sha-1"), PvpSignalCode.Kind.Offer, out err));
        Assert.Equal(PvpSignalCode.InvalidError, err);
        Assert.Null(PvpSignalCode.Parse(ChromeOffer.Replace("a=ice-ufrag:A53u\r\n", ""), PvpSignalCode.Kind.Offer, out _));
    }

    [Fact]
    public void HelloChecksVersionBuildRulesStartRuleAndCommit()
    {
        var mine = new PvpWire { T = "hello", V = 1, Build = "b", Rules = PvpProtocol.RulesFingerprint(Cards), StartRules = 2, Nonce = PvpProtocol.NewNonce() };
        var host = mine with { Nonce = null, Commit = PvpProtocol.Commit(PvpProtocol.NewNonce()) };
        Assert.Equal("", PvpProtocol.CheckHello(mine, host, theyAreHost: true));
        Assert.Equal("", PvpProtocol.CheckHello(host, mine, theyAreHost: false));
        Assert.Contains("較新", PvpProtocol.CheckHello(mine, host with { V = 2 }, true));
        Assert.Contains("較舊", PvpProtocol.CheckHello(mine, host with { V = 0 }, true));
        Assert.Equal("雙方的遊戲版本不同，請雙方都重新整理網頁後再試。", PvpProtocol.CheckHello(mine, host with { Build = "c" }, true));
        Assert.Contains("卡片資料", PvpProtocol.CheckHello(mine, host with { Rules = "x" }, true));
        Assert.Contains("開局規則", PvpProtocol.CheckHello(mine, host with { StartRules = 1 }, true));
        Assert.NotEqual("", PvpProtocol.CheckHello(mine, host with { Commit = null }, true));
        Assert.NotEqual("", PvpProtocol.CheckHello(host, mine with { Nonce = "zz" }, false));
    }

    [Fact]
    public void CommitRevealGivesTheSameSeedOnBothSidesAndCatchesACheat()
    {
        var h = PvpProtocol.NewNonce(); var g = PvpProtocol.NewNonce();
        Assert.True(PvpProtocol.CheckReveal(PvpProtocol.Commit(h), h));
        Assert.False(PvpProtocol.CheckReveal(PvpProtocol.Commit(h), PvpProtocol.NewNonce()));
        Assert.False(PvpProtocol.CheckReveal(PvpProtocol.Commit(h), null));
        Assert.Equal(PvpProtocol.Derive(h, g), PvpProtocol.Derive(h, g));
        Assert.NotEqual(PvpProtocol.Derive(h, g).Seed, PvpProtocol.Derive(h, PvpProtocol.NewNonce()).Seed);
        Assert.True(PvpProtocol.Derive(h, g).Seed >= 0);
        // Over many nonces both seats go first sometimes.
        var firsts = Enumerable.Range(0, 64).Select(_ => PvpProtocol.Derive(PvpProtocol.NewNonce(), g).HostFirst).Distinct().Count();
        Assert.Equal(2, firsts);
    }

    [Fact]
    public void DeckCodesMustBeLegal()
    {
        var starter = new DeckService(Cards, new MemoryStorage()).GetPlayerDecks().First(d => d.IsValid(Cards.GetCard, out _));
        Assert.NotNull(PvpProtocol.DeckFromCode(Cards, DeckCode.Encode(starter), out var err));
        Assert.Null(PvpProtocol.DeckFromCode(Cards, DeckCode.Encode(new Deck { Name = "x", MainWill = starter.MainWill, CardIds = starter.CardIds.Take(10).ToList() }), out err));
        Assert.Equal("對方的牌組不合規則。", err);
        Assert.Null(PvpProtocol.DeckFromCode(Cards, "garbage", out _));
    }

    [Fact]
    public void MovesSurviveTheWire()
    {
        var m = new PvpMove(3, PvpSeat.Guest, new WcgWeb.Models.Ranked.RankedAction("aic:attack", 1, 2, 0), "ABC");
        var back = PvpProtocol.ToMove(PvpProtocol.Deserialize(PvpProtocol.Serialize(PvpProtocol.FromMove(m)))!);
        Assert.Equal(m, back);
        Assert.Null(PvpProtocol.Deserialize("{not json"));
        Assert.Null(PvpProtocol.ToMove(new PvpWire { T = "move", Seat = 5, Move = m.Move, Hash = "x" }));
    }

    [Fact]
    public void P2pScriptIsShippedAndDoesNotShadowThePvpRoute()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..");
        Assert.True(File.Exists(Path.Combine(root, "WcgWeb", "wwwroot", "p2p", "rtc.js")));
        Assert.Contains("'p2p'", File.ReadAllText(Path.Combine(root, "SoulOath.Static", "tools", "prepare-assets.mjs")));
        // A static folder named like a page route makes the static host list the folder instead of the page.
        Assert.False(Directory.Exists(Path.Combine(root, "WcgWeb", "wwwroot", "pvp")));
        Assert.Contains("\"./p2p/rtc.js\"", File.ReadAllText(Path.Combine(root, "WcgWeb", "Services", "Pvp", "PvpSession.cs")));
    }
}
