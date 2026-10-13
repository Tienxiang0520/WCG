using System.Text.Json;
using WcgWeb.Services;

namespace WcgCloud;

public sealed record CloudSnapshot(Dictionary<string, string> Data, Dictionary<string, string> Preferences);
public sealed record StoredSave(bool Initialized, DateTimeOffset UpdatedAt, CloudSnapshot Save);
public sealed record CloudRead(bool Exists, string? Etag, CloudSnapshot? Save, DateTimeOffset? UpdatedAt);
public sealed record LeaseRequest(string Action, string LeaseId);
public sealed class CloudSaveValidator
{
    public const int MaxBytes = 5 * 1024 * 1024;
    private readonly CardDatabase cards;
    private readonly RankedDecks decks;
    public CloudSaveValidator()
    {
        string Read(string name)
        {
            using var stream = typeof(CloudSaveValidator).Assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        cards = new(Read("WcgCloud.Cards"), Read("WcgCloud.Presets"));
        decks = new(cards, Read("WcgCloud.RankedDecks"));
    }
    public void Validate(CloudSnapshot save)
    {
        if (save.Data is null || save.Preferences is null || save.Data.Count > 3 || save.Preferences.Count > 2 ||
            save.Data.Any(p => p.Key is not ("decks" or "ranked" or "profile") || p.Value is null)) throw new InvalidDataException("存檔項目不正確。");
        foreach (var (key, value) in save.Preferences)
        {
            if (key == "wcg.confirmEnergy" && value is "true" or "false") continue;
            if (key == "soul-oath.sidebar-width" && int.TryParse(value, out var width) && width is >= 200 and <= 360) continue;
            throw new InvalidDataException("存檔設定不正確。");
        }
        foreach (var value in save.Data.Values) { using var json = JsonDocument.Parse(value); }
        // Reuses deck validation, image validation and actual old/new ranked-journal replay validation.
        PlayerBackupValidator.Validate(JsonSerializer.Serialize(new { data = save.Data }), cards, decks);
    }
}
