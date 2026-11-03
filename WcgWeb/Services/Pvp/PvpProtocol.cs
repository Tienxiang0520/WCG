using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Models.Ranked;

namespace WcgWeb.Services.Pvp;

// One data-channel message (JSON). T: hello, start, move, ping, surrender, error, bye.
// Handshake: both send hello (protocol version, build, rules fingerprint, start rule, deck code; the host adds Commit =
// SHA-256 of its secret nonce, the guest adds its own Nonce). The host then sends start with its Nonce (the reveal) and
// the handshake Fingerprint. Seed and first player come from SHA-256(hostNonce + guestNonce), so neither side picks them:
// the host commits before seeing the guest's nonce and the guest checks the reveal against the commit.
public sealed record PvpWire
{
    public string T { get; init; } = "";
    public int V { get; init; }
    public string? Build { get; init; }
    public string? Rules { get; init; }
    public int StartRules { get; init; }
    public string? Deck { get; init; }
    public string? Commit { get; init; }
    public string? Nonce { get; init; }
    public string? Fingerprint { get; init; }
    public long Seq { get; init; }
    public int Seat { get; init; }
    public RankedAction? Move { get; init; }
    public string? Hash { get; init; }
    public string? Reason { get; init; }
}

public static class PvpProtocol
{
    public const int Version = 1;
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    public static string Serialize(PvpWire w) => JsonSerializer.Serialize(w, Json);
    public static PvpWire? Deserialize(string text)
    {
        if (text.Length > 64_000) return null;
        try { return JsonSerializer.Deserialize<PvpWire>(text, Json); } catch (JsonException) { return null; }
    }

    // Same value ranked uses for its era check (RankedSession.Fingerprint): any card change gives a new value.
    public static string RulesFingerprint(CardDatabase cards) =>
        "ranked-v06:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cards.AllCards))));
    // Build of the running game code: identical builds of the same source share it (deterministic compilation).
    public static string BuildId => typeof(PvpProtocol).Assembly.ManifestModule.ModuleVersionId.ToString("N")[..12];

    public static string NewNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    public static string Commit(string nonce) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("wcg-pvp-commit:" + nonce)));
    public static (int Seed, bool HostFirst) Derive(string hostNonce, string guestNonce)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes("wcg-pvp-seed:" + hostNonce + ":" + guestNonce));
        return (BitConverter.ToInt32(h, 0) & 0x7FFFFFFF, (h[4] & 1) == 0);
    }
    private static bool IsNonce(string? n) => n is { Length: 32 } && n.All(Uri.IsHexDigit);

    // Compares the other side's hello with ours. Returns "" or a zh-TW error key.
    public static string CheckHello(PvpWire mine, PvpWire theirs, bool theyAreHost)
    {
        if (theirs.T != "hello") return "對方送來無法辨識的連線資料。";
        if (theirs.V != mine.V) return theirs.V > mine.V ? "對方的遊戲版本較新，請重新整理網頁後再試。" : "對方的遊戲版本較舊，請對方重新整理網頁後再試。";
        if (theirs.Build != mine.Build) return "雙方的遊戲版本不同，請雙方都重新整理網頁後再試。";
        if (theirs.Rules != mine.Rules) return "雙方的卡片資料不同，請雙方都重新整理網頁後再試。";
        if (theirs.StartRules != mine.StartRules) return "雙方的開局規則不同，請雙方都重新整理網頁後再試。";
        if (theyAreHost ? theirs.Commit is not { Length: 64 } : !IsNonce(theirs.Nonce)) return "對方送來無法辨識的連線資料。";
        return "";
    }

    public static bool CheckReveal(string? commit, string? nonce) => IsNonce(nonce) && commit == Commit(nonce!);

    // A deck from a deck code that must be a legal 50-card deck in this catalog.
    public static Deck? DeckFromCode(CardDatabase cards, string? code, out string error)
    {
        var r = DeckCode.Decode(code, out error);
        if (r == null) return null;
        var deck = new Deck { Id = "pvp", Name = r.Name, MainWill = r.MainWill, CardIds = r.CardIds };
        if (r.CardIds.Any(id => cards.GetCard(id) == null) || !deck.IsValid(cards.GetCard, out _)) { error = "對方的牌組不合規則。"; return null; }
        return deck;
    }

    public static PvpWire FromMove(PvpMove m) => new() { T = "move", Seq = m.Seq, Seat = (int)m.Seat, Move = m.Move, Hash = m.StateHash };
    public static PvpMove? ToMove(PvpWire w) =>
        w.T == "move" && w.Move != null && w.Hash != null && w.Seat is 0 or 1 ? new PvpMove(w.Seq, (PvpSeat)w.Seat, w.Move, w.Hash) : null;
}
