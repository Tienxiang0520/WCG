using System.Globalization;
using System.Net;
using System.Text;

namespace WcgWeb.Services.Pvp;

// 連線代碼 (PvP PR 2): the WebRTC offer / answer as a short copy-paste code, so no signaling server is needed.
// Only the fields a data-channel-only connection needs survive: ICE ufrag/pwd, the DTLS sha-256 fingerprint, the DTLS
// role and the UDP candidates (non-trickle: gathered before the code is shown). Everything else is rebuilt by Rebuild.
// Format "WCGP1-" + base64url(binary): [version=1][kind 'O' offer / 'A' answer][setup 0 actpass,1 active,2 passive]
//   [ufrag len][ufrag][pwd len][pwd][32-byte fingerprint][candidate count] then per candidate
//   [type 0 host,1 srflx,2 prflx,3 relay][address tag 4 = IPv4 (4 bytes), 6 = IPv6 (16), 16 = mDNS "<uuid>.local" (16),
//   0 = [len][ASCII]][port big-endian 2][priority big-endian 4], then a CRC-16/CCITT of everything before it.
// The binary is already near-random (keys, hashes), so deflate would not shrink it; it is not compressed.
// Whitespace and line breaks anywhere are ignored. Errors are zh-TW localization keys.
public static class PvpSignalCode
{
    public const string Prefix = "WCGP1-";
    public const int MaxCandidates = 8, MaxCodeChars = 1500;
    public const string InvalidError = "連線代碼無效或不完整，請重新複製完整的代碼。";
    public const string NewerError = "這個連線代碼來自較新的版本，請雙方都重新整理網頁後再試。";
    public const string EmptyError = "請貼上連線代碼。";
    public const string ExpectOfferError = "這是回覆代碼，不是房間代碼。請貼上房主給你的房間代碼。";
    public const string ExpectAnswerError = "這是房間代碼，不是回覆代碼。請貼上對方回傳給你的回覆代碼。";
    public const string NoCandidateError = "找不到可用的網路位址，無法建立連線。";

    public enum Kind : byte { Offer = (byte)'O', Answer = (byte)'A' }
    public sealed record Candidate(byte Type, string Address, int Port, uint Priority);
    public sealed record Signal(Kind Kind, byte Setup, string Ufrag, string Pwd, byte[] Fingerprint, List<Candidate> Candidates);

    private static readonly string[] Types = ["host", "srflx", "prflx", "relay"];
    private static readonly string[] Setups = ["actpass", "active", "passive"];

    // Full SDP (from the browser) → the minimal fields, or null with an error key.
    public static Signal? Parse(string sdp, Kind kind, out string error)
    {
        error = InvalidError;
        string? ufrag = null, pwd = null; byte[]? fp = null; int setup = -1;
        var cands = new List<Candidate>();
        foreach (var raw in sdp.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("a=ice-ufrag:")) ufrag ??= line[12..];
            else if (line.StartsWith("a=ice-pwd:")) pwd ??= line[10..];
            else if (line.StartsWith("a=fingerprint:sha-256 ", StringComparison.OrdinalIgnoreCase))
                fp ??= ParseHex(line[22..]);
            else if (line.StartsWith("a=setup:")) { if (setup < 0) setup = Array.IndexOf(Setups, line[8..]); }
            else if (line.StartsWith("a=candidate:") && cands.Count < MaxCandidates && ParseCandidate(line[12..]) is { } c
                     && !cands.Any(x => x.Address == c.Address && x.Port == c.Port)) cands.Add(c);
        }
        if (ufrag is not { Length: > 0 and < 64 } || pwd is not { Length: > 0 and < 128 } || fp is not { Length: 32 } || setup < 0
            || !ufrag.All(IsIceChar) || !pwd.All(IsIceChar)) return null;
        if (cands.Count == 0) { error = NoCandidateError; return null; }
        error = "";
        return new(kind, (byte)setup, ufrag, pwd, fp, cands);
    }

    private static bool IsIceChar(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '+' or '/';

    private static Candidate? ParseCandidate(string body)
    {
        // foundation component transport priority address port "typ" type ...
        var f = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length < 8 || f[1] != "1" || !f[2].Equals("udp", StringComparison.OrdinalIgnoreCase) || f[6] != "typ") return null;
        var type = Array.IndexOf(Types, f[7]);
        if (type < 0 || !uint.TryParse(f[3], CultureInfo.InvariantCulture, out var prio) || !int.TryParse(f[5], CultureInfo.InvariantCulture, out var port) || port is <= 0 or > 65535) return null;
        if (AddressBytes(f[4]) == null && (f[4].Length > 64 || f[4].Any(ch => ch > 127 || char.IsWhiteSpace(ch)))) return null;
        return new((byte)type, f[4], port, prio);
    }

    private static (byte Tag, byte[] Bytes)? AddressBytes(string address)
    {
        if (address.EndsWith(".local", StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(address[..^6], "D", out var g)
            && g.ToString("D") == address[..^6].ToLowerInvariant()) return (16, GuidBytes(g));
        if (IPAddress.TryParse(address, out var ip) && ip.ToString() == address)
            return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? ((byte)4, ip.GetAddressBytes()) : ((byte)6, ip.GetAddressBytes());
        return null;
    }
    // RFC 4122 byte order (big-endian), so the code doesn't depend on .NET's mixed-endian Guid layout.
    private static byte[] GuidBytes(Guid g) => Convert.FromHexString(g.ToString("N"));
    private static string GuidText(byte[] b) => Guid.ParseExact(Convert.ToHexString(b), "N").ToString("D");

    public static string Encode(Signal s)
    {
        var b = new List<byte> { 1, (byte)s.Kind, s.Setup };
        void Str(string v) { var x = Encoding.ASCII.GetBytes(v); b.Add((byte)x.Length); b.AddRange(x); }
        Str(s.Ufrag); Str(s.Pwd); b.AddRange(s.Fingerprint);
        var cands = s.Candidates.Take(MaxCandidates).ToList();
        b.Add((byte)cands.Count);
        foreach (var c in cands)
        {
            b.Add(c.Type);
            if (AddressBytes(c.Address) is { } a) { b.Add(a.Tag); b.AddRange(a.Bytes); }
            else { b.Add(0); Str(c.Address); }
            b.Add((byte)(c.Port >> 8)); b.Add((byte)c.Port);
            b.Add((byte)(c.Priority >> 24)); b.Add((byte)(c.Priority >> 16)); b.Add((byte)(c.Priority >> 8)); b.Add((byte)c.Priority);
        }
        var crc = Crc16(b);
        b.Add((byte)(crc >> 8)); b.Add((byte)crc);
        return Prefix + Convert.ToBase64String(b.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // Code → fields; `expected` is the kind this screen wants (a pasted code of the other kind gets its own message).
    public static Signal? Decode(string? code, Kind expected, out string error)
    {
        error = "";
        var text = new string((code ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (text.Length == 0) { error = EmptyError; return null; }
        if (text.Length > MaxCodeChars) { error = InvalidError; return null; }
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = text.Length > 5 && text[..4].Equals("WCGP", StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(text[4]) && text[4] != '1'
                ? NewerError
                : text.StartsWith("WCGD", StringComparison.OrdinalIgnoreCase) ? "這是牌組代碼，不是連線代碼。" : InvalidError;
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
        if (raw.Length < 8 || Crc16(raw.Take(raw.Length - 2)) != (raw[^2] << 8 | raw[^1])) { error = InvalidError; return null; }
        if (raw[0] != 1) { error = raw[0] > 1 ? NewerError : InvalidError; return null; }
        try
        {
            int pos = 1, end = raw.Length - 2;
            byte Byte() => pos < end ? raw[pos++] : throw new InvalidDataException();
            byte[] Bytes(int n) { if (pos + n > end) throw new InvalidDataException(); var r = raw[pos..(pos + n)]; pos += n; return r; }
            string Str() => Encoding.ASCII.GetString(Bytes(Byte()));
            var kind = (Kind)Byte();
            if (kind is not (Kind.Offer or Kind.Answer)) throw new InvalidDataException();
            var setup = Byte(); if (setup >= Setups.Length) throw new InvalidDataException();
            var ufrag = Str(); var pwd = Str(); var fp = Bytes(32);
            if (ufrag.Length == 0 || pwd.Length == 0 || !ufrag.All(IsIceChar) || !pwd.All(IsIceChar)) throw new InvalidDataException();
            int n = Byte(); if (n is 0 or > MaxCandidates) throw new InvalidDataException();
            var cands = new List<Candidate>();
            for (int i = 0; i < n; i++)
            {
                var type = Byte(); if (type >= Types.Length) throw new InvalidDataException();
                var tag = Byte();
                var address = tag switch
                {
                    4 or 6 => new IPAddress(Bytes(tag == 4 ? 4 : 16)).ToString(),
                    16 => GuidText(Bytes(16)) + ".local",
                    0 => Str(),
                    _ => throw new InvalidDataException()
                };
                if (address.Length == 0 || address.Any(ch => ch <= ' ' || ch > 126)) throw new InvalidDataException();
                int port = Byte() << 8 | Byte(); if (port == 0) throw new InvalidDataException();
                uint prio = (uint)Byte() << 24 | (uint)Byte() << 16 | (uint)Byte() << 8 | Byte();
                cands.Add(new(type, address, port, prio));
            }
            if (pos != end) throw new InvalidDataException();
            if (kind != expected) { error = expected == Kind.Offer ? ExpectOfferError : ExpectAnswerError; return null; }
            return new(kind, setup, ufrag, pwd, fp, cands);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException) { error = InvalidError; return null; }
    }

    // A minimal data-channel SDP the browser accepts as the remote description.
    public static string Rebuild(Signal s)
    {
        var sb = new StringBuilder();
        void L(string v) => sb.Append(v).Append("\r\n");
        L("v=0"); L("o=- 4611731400430051336 2 IN IP4 127.0.0.1"); L("s=-"); L("t=0 0");
        L("a=group:BUNDLE 0"); L("a=extmap-allow-mixed"); L("a=msid-semantic: WMS");
        L("m=application 9 UDP/DTLS/SCTP webrtc-datachannel"); L("c=IN IP4 0.0.0.0");
        for (int i = 0; i < s.Candidates.Count; i++)
        {
            var c = s.Candidates[i];
            var extra = c.Type == 0 ? "" : " raddr 0.0.0.0 rport 0";
            L($"a=candidate:{i + 1} 1 udp {c.Priority.ToString(CultureInfo.InvariantCulture)} {c.Address} {c.Port.ToString(CultureInfo.InvariantCulture)} typ {Types[c.Type]}{extra}");
        }
        L("a=end-of-candidates");
        L("a=ice-ufrag:" + s.Ufrag); L("a=ice-pwd:" + s.Pwd);
        L("a=fingerprint:sha-256 " + string.Join(":", s.Fingerprint.Select(x => x.ToString("X2", CultureInfo.InvariantCulture))));
        L("a=setup:" + Setups[s.Setup]); L("a=mid:0"); L("a=sctp-port:5000"); L("a=max-message-size:262144");
        return sb.ToString();
    }

    private static byte[]? ParseHex(string text)
    {
        var parts = text.Trim().Split(':');
        if (parts.Length != 32) return null;
        var r = new byte[32];
        for (int i = 0; i < 32; i++) if (!byte.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r[i])) return null;
        return r;
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
