using System.Text;
using WcgWeb.Models;

namespace WcgWeb.Services;

// 牌組代碼: a short shareable text form of one deck. It stores only card ids, counts, the main will and an optional name,
// never card text, so card revisions don't break old codes.
// Format "WCGD1-" + base64url(binary): [version=1][main will 0..5][name length][name UTF-8]
//   then runs of [varint card number][count] ("WCG-054" → 54), or [0][length][ASCII id] for ids that aren't WCG-<digits>,
//   then a CRC-16/CCITT of everything before it. Whitespace and line breaks anywhere are ignored.
// A future WCGD2 must keep this decoder.
public static class DeckCode
{
    public const string Prefix = "WCGD1-";
    public const int MaxNameChars = 24, MaxCodeChars = 2000;
    public const string InvalidError = "代碼無效或不完整。";

    public sealed record Result(string Name, string MainWill, List<string> CardIds);

    public static string Encode(Deck deck)
    {
        var bytes = new List<byte> { 1, (byte)(Deck.Wills.ToList().IndexOf(deck.MainWill) + 1) };
        var name = (deck.Name ?? "").Trim();
        if (name.Length > MaxNameChars) name = name[..MaxNameChars];
        var nameBytes = Encoding.UTF8.GetBytes(name);
        bytes.Add((byte)nameBytes.Length); bytes.AddRange(nameBytes);
        foreach (var g in deck.CardIds.Select(CardIdentifier.Canonical).GroupBy(id => id).OrderBy(g => SortKey(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            var number = NumberOf(g.Key);
            if (number > 0) WriteVarint(bytes, number);
            else
            {
                var raw = Encoding.ASCII.GetBytes(g.Key);
                if (raw.Length is 0 or > 64 || g.Key.Any(c => c > 127)) continue;
                bytes.Add(0); bytes.Add((byte)raw.Length); bytes.AddRange(raw);
            }
            bytes.Add((byte)Math.Min(255, g.Count()));
        }
        var crc = Crc16(bytes);
        bytes.Add((byte)(crc >> 8)); bytes.Add((byte)crc);
        return Prefix + Convert.ToBase64String(bytes.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // Returns the decoded deck contents, or null with a zh-TW error (a localization key).
    public static Result? Decode(string? code, out string error)
    {
        error = "";
        var text = new string((code ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (text.Length == 0) { error = "請貼上牌組代碼。"; return null; }
        if (text.Length > MaxCodeChars) { error = "牌組代碼過長。"; return null; }
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = text.Length > 5 && text[..4].Equals("WCGD", StringComparison.OrdinalIgnoreCase) && char.IsDigit(text[4]) && text[4] != '1'
                ? "這個牌組代碼來自較新的版本，請更新遊戲後再試。" : InvalidError;
            return null;
        }
        byte[] raw;
        try
        {
            var b64 = text[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            if (b64.Length % 4 == 1) { error = InvalidError; return null; }
            raw = Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
        }
        catch (FormatException) { error = InvalidError; return null; }
        if (raw.Length < 5 || Crc16(raw.Take(raw.Length - 2)) != (raw[^2] << 8 | raw[^1]) || raw[0] != 1) { error = InvalidError; return null; }
        try
        {
            int pos = 1, end = raw.Length - 2;
            int will = raw[pos++];
            if (will > Deck.Wills.Count) throw new InvalidDataException();
            int nameLen = raw[pos++];
            if (pos + nameLen > end) throw new InvalidDataException();
            var name = Encoding.UTF8.GetString(raw, pos, nameLen); pos += nameLen;
            var ids = new List<string>();
            while (pos < end)
            {
                int number = ReadVarint(raw, ref pos, end);
                string id;
                if (number == 0)
                {
                    if (pos >= end) throw new InvalidDataException();
                    int len = raw[pos++];
                    if (len == 0 || pos + len > end) throw new InvalidDataException();
                    id = CardIdentifier.Canonical(Encoding.ASCII.GetString(raw, pos, len)); pos += len;
                }
                else id = $"WCG-{number:000}";
                if (pos >= end) throw new InvalidDataException();
                int count = raw[pos++];
                if (count == 0) throw new InvalidDataException();
                ids.AddRange(Enumerable.Repeat(id, count));
                if (ids.Count > 200) throw new InvalidDataException();
            }
            return new Result(name, will == 0 ? "" : Deck.Wills[will - 1], ids);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException) { error = InvalidError; return null; }
    }

    private static int NumberOf(string id) =>
        id.StartsWith(CardIdentifier.CurrentPrefix, StringComparison.Ordinal) && id.Length is > 4 and <= 10 && id[4..].All(char.IsAsciiDigit)
        && int.TryParse(id[4..], out var n) && n > 0 && $"WCG-{n:000}" == id ? n : 0;
    private static int SortKey(string id) { var n = NumberOf(id); return n == 0 ? int.MaxValue : n; }

    private static void WriteVarint(List<byte> bytes, int value)
    {
        do { var b = (byte)(value & 0x7F); value >>= 7; bytes.Add(value != 0 ? (byte)(b | 0x80) : b); } while (value != 0);
    }
    private static int ReadVarint(byte[] raw, ref int pos, int end)
    {
        int value = 0, shift = 0;
        while (true)
        {
            if (pos >= end || shift > 21) throw new InvalidDataException();
            var b = raw[pos++]; value |= (b & 0x7F) << shift; shift += 7;
            if ((b & 0x80) == 0) return value;
        }
    }
    private static ushort Crc16(IEnumerable<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var d in data)
        {
            crc ^= (ushort)(d << 8);
            for (int i = 0; i < 8; i++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }
}
