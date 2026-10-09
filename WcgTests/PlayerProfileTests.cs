using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Services;
namespace WcgTests;
public sealed class PlayerProfileTests : IDisposable
{
    static readonly byte[] Webp = [.."RIFF"u8, 30, 0, 0, 0, .."WEBPVP8 "u8, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 1, 2, 3, 4];
    static string Url(string mime, byte[] bytes) => $"data:image/{mime};base64," + Convert.ToBase64String(bytes);
    readonly string root = Path.Combine(Path.GetTempPath(), "wcg-profile-" + Guid.NewGuid());
    sealed class MemoryStorage : IPlayerStorage
    {
        public object Gate { get; } = new();
        public Dictionary<string, string> Values { get; } = new();
        public string? Read(string key) => Values.GetValueOrDefault(key);
        public void Write(string key, string value, string? expected)
        {
            if (Values.GetValueOrDefault(key) != expected) throw new IOException("stale");
            Values[key] = value;
        }
    }

    [Fact] public void OldSaveWithoutProfileShowsDefaultAvatar()
    {
        var store = new PlayerProfileStore(new MemoryStorage());
        Assert.Equal(PlayerAvatars.Default, store.Avatar);
        Assert.Equal("battle/avatars/player.svg", store.AvatarUrl);
        Assert.True(store.IsDefault); Assert.Null(store.LastError);
    }
    [Fact] public void BuiltInChoicePersistsAndResetRestoresDefault()
    {
        var storage = new MemoryStorage(); var store = new PlayerProfileStore(storage); var changes = 0; store.Changed += () => changes++;
        store.SetAvatar("builtin:wolf");
        Assert.Equal("battle/avatars/wolf.svg", new PlayerProfileStore(storage).AvatarUrl);
        Assert.Equal("霜牙狼", store.Label);
        store.Reset();
        Assert.True(new PlayerProfileStore(storage).IsDefault); Assert.Equal(2, changes);
        Assert.Equal(8, PlayerAvatars.BuiltIn.Count);
        Assert.All(PlayerAvatars.BuiltIn, a => Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/wwwroot/battle/avatars", a.File))));
    }
    [Fact] public void UploadedImagesMustMatchTheirDeclaredFormat()
    {
        var store = new PlayerProfileStore(new MemoryStorage());
        store.SetAvatar(Url("webp", Webp)); Assert.True(store.IsCustom); Assert.StartsWith("data:image/webp;base64,", store.AvatarUrl);
        store.SetAvatar(Url("png", Png)); Assert.Equal("自訂圖片", store.Label);
        foreach (var bad in new[] { Url("jpeg", Webp), Url("png", Webp), Url("svg+xml", "<svg onload=alert(1)>aaaaaaaaaaaaaaaaaa"u8.ToArray()),
                     "data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==", "data:image/webp;base64,@@@not-base64@@@@@@@@@",
                     "javascript:alert(1)", "https://example.com/a.png", "builtin:nope", "", "builtin:../../secret" })
            Assert.Throws<InvalidDataException>(() => store.SetAvatar(bad));
        Assert.Equal(Url("png", Png), store.Avatar);
    }
    [Fact] public void OversizedUploadIsRejectedAndPreviousAvatarKept()
    {
        var storage = new MemoryStorage(); var store = new PlayerProfileStore(storage); store.SetAvatar("builtin:sage");
        var huge = new byte[PlayerAvatars.MaxStoredChars]; Webp.CopyTo(huge, 0);
        var error = Assert.Throws<InvalidDataException>(() => store.SetAvatar(Url("webp", huge)));
        Assert.Contains("300 KB", error.Message);
        Assert.Equal("builtin:sage", new PlayerProfileStore(storage).Avatar);
    }
    [Fact] public void DamagedProfileFallsBackWithoutThrowingAndCanBeReplaced()
    {
        foreach (var damaged in new[] { "broken", "{\"Avatar\":\"javascript:alert(1)\"}", "null" })
        {
            var storage = new MemoryStorage(); storage.Values["profile"] = damaged;
            var store = new PlayerProfileStore(storage);
            Assert.Equal("battle/avatars/player.svg", store.AvatarUrl); Assert.NotNull(store.LastError);
            store.SetAvatar("builtin:ember"); Assert.Equal("builtin:ember", new PlayerProfileStore(storage).Avatar);
        }
    }
    [Fact] public void StaleWriteFromAnotherTabIsRejected()
    {
        var storage = new MemoryStorage(); var a = new PlayerProfileStore(storage);
        a.SetAvatar("builtin:wolf");
        var staleStorage = new StaleStorage(storage);
        Assert.Throws<IOException>(() => new PlayerProfileStore(staleStorage).SetAvatar("builtin:abyss"));
        Assert.Equal("builtin:wolf", new PlayerProfileStore(storage).Avatar);
    }
    sealed class StaleStorage(MemoryStorage inner) : IPlayerStorage
    {
        public object Gate { get; } = new();
        public string? Read(string key) => null;
        public void Write(string key, string value, string? expected) => inner.Write(key, value, expected);
    }
    [Fact] public void BackupValidationAcceptsAvatarAndRejectsUnsafeOne()
    {
        var data = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/Data"));
        var cards = new CardDatabase(File.ReadAllText(Path.Combine(data, "cards.json")), File.ReadAllText(Path.Combine(data, "preset_decks.json")));
        var opponents = new RankedDecks(cards, File.ReadAllText(Path.Combine(data, "ranked_decks.json")));
        string Backup(string profile) => JsonSerializer.Serialize(new { format = "soul-oath-local", version = 1, data = new Dictionary<string, string> { ["profile"] = profile }, preferences = new { } });
        PlayerBackupValidator.Validate(Backup(JsonSerializer.Serialize(new PlayerProfile { Avatar = Url("webp", Webp) })), cards, opponents);
        PlayerBackupValidator.Validate(Backup("{\"Avatar\":\"builtin:grove\"}"), cards, opponents);
        Assert.Throws<InvalidDataException>(() => PlayerBackupValidator.Validate(Backup("{\"Avatar\":\"data:text/html;base64,PGI+aGk8L2I+aGVsbG8gd29ybGQ=\"}"), cards, opponents));
        Assert.Throws<InvalidDataException>(() => PlayerBackupValidator.Validate(Backup("not json"), cards, opponents));
    }
    [Fact] public void AvatarAndTieredRankedSaveCoexistInOneSaveAndBackup()
    {
        var data = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/Data"));
        var cards = new CardDatabase(File.ReadAllText(Path.Combine(data, "cards.json")), File.ReadAllText(Path.Combine(data, "preset_decks.json")));
        var opponents = new RankedDecks(cards, File.ReadAllText(Path.Combine(data, "ranked_decks.json")));
        var avatar = JsonSerializer.Serialize(new PlayerProfile { Avatar = Url("webp", Webp) });
        // An old (v1) ranked save next to an avatar: the backup is accepted and the ranked reset leaves the avatar alone.
        var oldRanked = "{\"Version\":1,\"Season\":\"2026-10\",\"Stars\":7,\"Wins\":7}";
        PlayerBackupValidator.Validate(JsonSerializer.Serialize(new { format = "soul-oath-local", version = 1, data = new Dictionary<string, string> { ["ranked"] = oldRanked, ["profile"] = avatar }, preferences = new { } }), cards, opponents);
        var storage = new MemoryStorage(); storage.Values["ranked"] = oldRanked; storage.Values["profile"] = avatar;
        var ranked = new RankedStore(storage); var loaded = ranked.Load();
        Assert.True(ranked.ResetOnLoad); ranked.Save(loaded);
        Assert.Contains($"\"Version\": {WcgWeb.Models.Ranked.RankedRules.ProfileVersion}", storage.Values["ranked"]);
        Assert.Equal(avatar, storage.Values["profile"]);
        var profile = new PlayerProfileStore(storage); Assert.True(profile.IsCustom);
        profile.Reset(); Assert.Equal(0, new RankedStore(storage).Load().Stars);
    }
    [Fact] public void ServerHostKeepsAvatarInItsOwnPlayerFile()
    {
        Directory.CreateDirectory(Path.Combine(root, "Data"));
        var env = new Mock<IWebHostEnvironment>(); env.Setup(x => x.ContentRootPath).Returns(root);
        new PlayerProfileStore(env.Object).SetAvatar("builtin:aegis");
        Assert.Equal("builtin:aegis", new PlayerProfileStore(env.Object).Avatar);
        Assert.Contains("aegis", File.ReadAllText(Path.Combine(root, "Data", "player_profile.json")));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
