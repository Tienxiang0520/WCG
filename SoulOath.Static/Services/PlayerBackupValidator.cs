using System.Text.Json;
using WcgWeb.Models;
using Microsoft.Extensions.Logging.Abstractions;
namespace WcgWeb.Services;

public static class PlayerBackupValidator
{
    public static void Validate(string text, CardDatabase cards, RankedDecks opponents)
    {
        using var document = JsonDocument.Parse(text);
        var data = document.RootElement.GetProperty("data");
        var values = data.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var storage = new SnapshotStorage(values);
        var decks = new DeckService(cards, storage);
        foreach (var deck in decks.GetCustomDecks())
        {
            if (!deck.IsValid(cards.GetCard, out var error)) throw new InvalidDataException(error);
            if (cards.PresetDecks.Any(d => d.Id == deck.Id)) throw new InvalidDataException("自訂牌組不可覆蓋官方預組。");
        }
        if (decks.LastStorageError != null) throw new InvalidDataException(decks.LastStorageError);
        if (values.TryGetValue("profile", out var avatarProfile)) PlayerProfileStore.Parse(avatarProfile);
        if (!values.ContainsKey("ranked")) return;
        var store = new RankedStore(storage);
        var profile = store.Load();
        if (profile.Match is { } match && (!match.PlayerDeck.IsValid(cards.GetCard, out _) || !match.ComputerDeck.IsValid(cards.GetCard, out _)))
            throw new InvalidDataException("備份對局包含不合法牌組。");
        var session = new RankedSession(cards, decks, store, opponents, NullLogger<BattleCoordinator>.Instance);
        try { if (session.Error != "") throw new InvalidDataException("天梯對局無法還原，原存檔保留。"); }
        finally { session.Coordinator.Dispose(); }
    }
    private sealed class SnapshotStorage(Dictionary<string, string> values) : IPlayerStorage
    {
        public object Gate { get; } = new();
        public string? Read(string key) => values.GetValueOrDefault(key);
        public void Write(string key, string value, string? expected) => values[key] = value;
    }
}
