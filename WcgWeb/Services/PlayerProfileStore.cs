using System.Text.Json;
namespace WcgWeb.Services;

public sealed record BuiltInAvatar(string Id, string Label, string File);

// Player avatar: a built-in picture id or a small, already cropped image stored next to the other player data.
public static class PlayerAvatars
{
    public const string DefaultId = "warden";
    public const string BuiltInPrefix = "builtin:";
    public const int MaxStoredChars = 300 * 1024;
    public static readonly IReadOnlyList<BuiltInAvatar> BuiltIn =
    [
        new("warden", "守誓衛士", "player.svg"), new("ember", "烈焰狂戰士", "ember.svg"), new("sage", "星辰賢者", "sage.svg"),
        new("grove", "林語德魯伊", "grove.svg"), new("aegis", "金曜聖騎", "aegis.svg"), new("abyss", "深淵隱者", "abyss.svg"),
        new("wolf", "霜牙狼", "wolf.svg"), new("lantern", "燈靈", "lantern.svg")
    ];
    public static string Default => BuiltInPrefix + DefaultId;
    private static readonly (string Header, Func<ReadOnlyMemory<byte>, bool> Matches)[] Formats =
    [
        ("data:image/webp;base64", b => b.Length > 12 && b.Span[..4].SequenceEqual("RIFF"u8) && b.Span[8..12].SequenceEqual("WEBP"u8)),
        ("data:image/jpeg;base64", b => b.Length > 3 && b.Span[0] == 0xFF && b.Span[1] == 0xD8 && b.Span[2] == 0xFF),
        ("data:image/png;base64", b => b.Length > 8 && b.Span[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
    ];
    public static BuiltInAvatar? Find(string? value) =>
        value?.StartsWith(BuiltInPrefix, StringComparison.Ordinal) == true ? BuiltIn.FirstOrDefault(a => a.Id == value[BuiltInPrefix.Length..]) : null;
    public static bool IsUpload(string value) => value.StartsWith("data:", StringComparison.Ordinal);
    // Only a known picture id or a base64 webp/jpeg/png whose bytes match its declared type is accepted.
    public static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) throw new InvalidDataException("頭像資料是空的。");
        if (Find(value) is { } builtIn) return BuiltInPrefix + builtIn.Id;
        if (!IsUpload(value)) throw new InvalidDataException("不認得這個頭像。");
        if (value.Length > MaxStoredChars) throw new InvalidDataException("頭像圖片過大（上限 300 KB）。");
        var comma = value.IndexOf(',');
        var format = comma < 0 ? default : Formats.FirstOrDefault(f => f.Header == value[..comma]);
        if (format.Header == null) throw new InvalidDataException("頭像只接受 WebP、JPEG 或 PNG 圖片。");
        var payload = value[(comma + 1)..];
        var bytes = new byte[payload.Length * 3 / 4 + 3];
        if (payload.Length < 16 || !Convert.TryFromBase64String(payload, bytes, out var length)) throw new InvalidDataException("頭像圖片內容損壞。");
        if (!format.Matches(bytes.AsMemory(0, length))) throw new InvalidDataException("頭像圖片格式與內容不符。");
        return value;
    }
    public static string Url(string? value) => Find(value) is { } builtIn ? "battle/avatars/" + builtIn.File
        : value != null && IsUpload(value) ? value : "battle/avatars/player.svg";
}

public sealed class PlayerProfile { public string Avatar { get; set; } = PlayerAvatars.Default; }

public sealed partial class PlayerProfileStore(IPlayerStorage storage)
{
    private const string Key = "profile";
    private string? avatar;
    public event Action? Changed;
    public string? LastError { get; private set; }
    public string Avatar => avatar ??= Load();
    public string AvatarUrl => PlayerAvatars.Url(Avatar);
    public bool IsCustom => PlayerAvatars.IsUpload(Avatar);
    public bool IsDefault => Avatar == PlayerAvatars.Default;
    public string Label => PlayerAvatars.Find(Avatar)?.Label ?? "自訂圖片";

    // Older saves have no profile section; damaged profile data shows the default without blocking the game.
    public static PlayerProfile Parse(string? json)
    {
        if (json == null) return new();
        PlayerProfile? profile;
        try { profile = JsonSerializer.Deserialize<PlayerProfile>(json); }
        catch (JsonException ex) { throw new InvalidDataException("頭像存檔格式不正確。", ex); }
        if (profile == null) throw new InvalidDataException("頭像存檔格式不正確。");
        profile.Avatar = PlayerAvatars.Normalize(profile.Avatar);
        return profile;
    }
    private string Load()
    {
        try { LastError = null; return Parse(storage.Read(Key)).Avatar; }
        catch (Exception ex) when (ex is InvalidDataException or IOException) { LastError = ex.Message; return PlayerAvatars.Default; }
    }
    public void Reload() { avatar = null; _ = Avatar; Changed?.Invoke(); }
    public void SetAvatar(string value)
    {
        var normalized = PlayerAvatars.Normalize(value);
        lock (storage.Gate)
        {
            var current = storage.Read(Key);
            storage.Write(Key, JsonSerializer.Serialize(new PlayerProfile { Avatar = normalized }), current);
        }
        avatar = normalized; LastError = null;
        Changed?.Invoke();
    }
    public void Reset() => SetAvatar(PlayerAvatars.Default);
}
