using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WcgWeb.Models;
using WcgWeb.Models.Ranked;

namespace WcgWeb.Services;

// 對局代碼: a self-contained, shareable text form of one ranked match (finished or in progress).
// It carries exactly what 對局回顧 needs to re-simulate the match offline: seed, tier (AI level), first player,
// rules/catalog fingerprint (selects the card era), both deck snapshots and the action journal; each journal
// action keeps its own RulesVersion (AI revision), so old-era codes replay with their era's rules and AI.
// Format: "WCG1-" + base64url(zlib(UTF-8 JSON)). Whitespace and line breaks anywhere are ignored.
public static class MatchCode
{
    public const string Prefix = "WCG1-";
    private const int MaxCodeChars = 200_000, MaxJsonBytes = 1_000_000;
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private sealed record CodeDeck(string N, string W, string I, string C);
    private sealed record Payload(int V, string R, int T, int S, bool F, CodeDeck P, CodeDeck O, string A,
        string? Ar = null, bool? Won = null, int? B = null, int? Af = null, string? Re = null, int? Tu = null, string? Se = null, Guid? Id = null, bool? Live = null);

    public static string Encode(RankedRecord r, bool inProgress = false)
    {
        var payload = new Payload(1, r.Rules, r.Tier, r.Seed, r.PlayerFirst, Pack(r.PlayerDeck), Pack(r.ComputerDeck), r.Actions,
            r.Archetype == "" ? null : r.Archetype, inProgress ? null : r.Won, r.Before, inProgress ? null : r.After, r.Reason == "" ? null : r.Reason,
            r.Turns, r.Season == "" ? null : r.Season, r.Id, inProgress ? true : null);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) z.Write(bytes);
        return Prefix + Convert.ToBase64String(output.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // Returns the record, or null with a zh-TW error message (a localization key) explaining what is wrong.
    public static RankedRecord? Decode(string? code, out string error, out bool inProgress)
    {
        error = ""; inProgress = false;
        var text = new string((code ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (text.Length == 0) { error = "請貼上對局代碼。"; return null; }
        if (text.Length > MaxCodeChars) { error = "對局代碼過長。"; return null; }
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = text.Length > 4 && text[..3].Equals("WCG", StringComparison.OrdinalIgnoreCase) && char.IsDigit(text[3]) ? "這個對局代碼來自較新的版本，請更新遊戲後再試。" : "這不是魂誓對局代碼（應以 WCG1- 開頭）。";
            return null;
        }
        byte[] raw;
        try
        {
            var b64 = text[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            if (b64.Length % 4 == 1) { error = "對局代碼已損壞或複製不完整。"; return null; }
            raw = Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
        }
        catch (FormatException) { error = "對局代碼含有無效字元，可能複製不完整。"; return null; }
        Payload? p;
        try
        {
            using var input = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
            using var json = new MemoryStream(); var buffer = new byte[16384]; int n;
            while ((n = input.Read(buffer)) > 0) { if (json.Length + n > MaxJsonBytes) { error = "對局代碼過長。"; return null; } json.Write(buffer, 0, n); }
            p = JsonSerializer.Deserialize<Payload>(json.ToArray(), Json);
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or IOException) { error = "對局代碼已損壞或複製不完整。"; return null; }
        if (p == null || p.P == null || p.O == null || p.R == null || p.A == null) { error = "對局代碼已損壞或複製不完整。"; return null; }
        if (p.V != 1) { error = "這個對局代碼來自較新的版本，請更新遊戲後再試。"; return null; }
        if (p.T is < 0 or > 5 || !p.R.StartsWith("ranked-", StringComparison.Ordinal)) { error = "對局代碼的內容不正確。"; return null; }
        RankedRecord record;
        try
        {
            RankedRecord.Decode(p.A);
            record = new()
            {
                Id = p.Id ?? Guid.Empty, Season = p.Se ?? "", Tier = p.T, Seed = p.S, PlayerFirst = p.F, Rules = p.R,
                PlayerDeck = Unpack(p.P), ComputerDeck = Unpack(p.O), Archetype = p.Ar ?? "", Won = p.Won ?? false,
                Before = p.B ?? 0, After = p.Af ?? p.B ?? 0, Reason = p.Re ?? "", Turns = p.Tu ?? 0, Actions = p.A
            };
        }
        catch (InvalidDataException) { error = "對局代碼的內容不正確。"; return null; }
        inProgress = p.Live == true;
        return record;
    }

    // Card lists are stored as "number*count" runs (the WCG- prefix is implied), keeping deck order stable.
    private static CodeDeck Pack(Deck d)
    {
        var runs = new List<string>();
        for (int i = 0; i < d.CardIds.Count;)
        {
            int j = i; while (j < d.CardIds.Count && d.CardIds[j] == d.CardIds[i]) j++;
            var id = d.CardIds[i].StartsWith("WCG-", StringComparison.Ordinal) ? d.CardIds[i][4..] : "!" + d.CardIds[i];
            runs.Add(j - i == 1 ? id : $"{id}*{j - i}"); i = j;
        }
        return new(d.Name, d.MainWill, d.Id, string.Join(',', runs));
    }
    private static Deck Unpack(CodeDeck c)
    {
        var ids = new List<string>();
        if (c.C is not { Length: > 0 }) throw new InvalidDataException();
        foreach (var run in c.C.Split(','))
        {
            var parts = run.Split('*'); int count = 1;
            if (parts.Length > 2 || parts[0].Length == 0 || (parts.Length == 2 && (!int.TryParse(parts[1], out count) || count is < 1 or > 60))) throw new InvalidDataException();
            var id = parts[0][0] == '!' ? parts[0][1..] : "WCG-" + parts[0];
            ids.AddRange(Enumerable.Repeat(id, count));
            if (ids.Count > 60) throw new InvalidDataException();
        }
        return new Deck { Id = c.I ?? "", Name = c.N ?? "", MainWill = c.W ?? "", CardIds = ids };
    }
}
