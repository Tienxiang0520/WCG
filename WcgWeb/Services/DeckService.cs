using System.Text.Json;
using WcgWeb.Models;

namespace WcgWeb.Services;

public partial class DeckService
{
    private static readonly JsonSerializerOptions SaveJson = new() { WriteIndented = true };
    private readonly CardDatabase _cardDb;
    private readonly IPlayerStorage _storage;
    private readonly RankedDecks? _referenceDecks;
    private readonly object _gate;
    private string? lastRead;

    public string? LastStorageError { get; private set; }

    public DeckService(CardDatabase cardDb, IPlayerStorage storage, RankedDecks? referenceDecks = null)
    {
        _cardDb = cardDb;
        _storage = storage;
        _gate = storage.Gate;
        _referenceDecks = referenceDecks;
    }

    private List<Deck> Read(bool strict = false)
    {
        try
        {
            LastStorageError = null;
            lastRead = _storage.Read("decks");
            if (lastRead is null) return [];

            var decks = JsonSerializer.Deserialize<List<Deck>>(lastRead)
                ?? throw new JsonException("牌組檔內容為空。");
            if (decks.Any(d => d == null || d.CardIds == null || d.Version < 0 || string.IsNullOrWhiteSpace(d.Id))
                || decks.Select(d => d.Id).Distinct().Count() != decks.Count)
                throw new JsonException("牌組資料結構或 ID 不正確。");
            return decks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LastStorageError = $"牌組存檔讀取失敗，原資料保留：{ex.Message}";
            if (strict) throw new InvalidOperationException(LastStorageError, ex);
            return [];
        }
    }

    private void Write(List<Deck> decks)
    {
        var json = JsonSerializer.Serialize(decks, SaveJson);
        _storage.Write("decks", json, lastRead);
        lastRead = json;
        LastStorageError = null;
    }

    public static Deck Copy(Deck deck) => new()
    {
        Id = deck.Id,
        Name = deck.Name,
        Description = deck.Description,
        MainWill = deck.MainWill,
        Version = deck.Version,
        CardIds = [.. deck.CardIds]
    };

    // Templates stay inside the service. Only copies are returned to callers who may edit them.
    private IEnumerable<Deck> ReferenceTemplates => _referenceDecks?.Pool(5).Select(entry => entry.Deck) ?? [];

    public List<Deck> GetCustomDecks()
    {
        lock (_gate) return Read().Select(Copy).ToList();
    }

    // Only a genuinely new save gets a starter. Empty/deleted or unreadable saves are left intact.
    public List<Deck> GetPlayerDecks()
    {
        lock (_gate)
        {
            var decks = Read();
            if (lastRead != null || LastStorageError != null) return decks.Select(Copy).ToList();
            var starter = CreateStarterDeck();
            try
            {
                if (!starter.IsValid(_cardDb.GetCard, out var error)) throw new InvalidOperationException(error);
                starter.Version = 1;
                Write([starter]);
                return [Copy(starter)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                LastStorageError = $"入門牌組儲存失敗，原資料保留：{ex.Message}";
                return [];
            }
        }
    }

    public static Deck CreateStarterDeck() => new()
    {
        Name = "我的入門牌組",
        Description = "以普通怪物、抽牌與簡單法術練習出牌和交戰，可自由修改。",
        MainWill = "狂怒",
        CardIds = new[] { "101", "103", "106", "110", "003", "114", "116", "015", "007", "109", "121", "012", "014" }
            .SelectMany((id, index) => Enumerable.Repeat($"WCG-{id}", index < 11 ? 4 : 3)).ToList()
    };

    public IReadOnlyList<Deck> GetReferenceDecks() => ReferenceTemplates.Select(Copy).ToArray();

    public List<Deck> GetAllAvailableDecks() =>
        [.. GetCustomDecks(), .. _cardDb.PresetDecks.Select(Copy), .. GetReferenceDecks()];

    public Deck? GetDeck(string id)
    {
        lock (_gate)
        {
            var deck = Read().FirstOrDefault(d => d.Id == id)
                ?? _cardDb.PresetDecks.FirstOrDefault(d => d.Id == id)
                ?? ReferenceTemplates.FirstOrDefault(d => d.Id == id);
            return deck is null ? null : Copy(deck);
        }
    }

    public void SaveDeck(Deck deck)
    {
        if (string.IsNullOrWhiteSpace(deck.Id) || deck.Version < 0)
            throw new ArgumentException("牌組 ID 或版本不正確。");
        if (!deck.IsValid(_cardDb.GetCard, out var error)) throw new ArgumentException(error);
        if (_cardDb.PresetDecks.Any(d => d.Id == deck.Id) || ReferenceTemplates.Any(d => d.Id == deck.Id))
            throw new ArgumentException("請複製預設牌組後另存。");

        lock (_gate)
        {
            var decks = Read(true);
            var index = decks.FindIndex(d => d.Id == deck.Id);
            if ((index >= 0 && decks[index].Version != deck.Version) || (index < 0 && deck.Version != 0))
                throw new InvalidOperationException("牌組已由其他頁面更新或刪除，請重新載入後再編輯。");

            var saved = Copy(deck);
            saved.Version = deck.Version + 1;
            if (index >= 0) decks[index] = saved;
            else decks.Add(saved);
            Write(decks);
            deck.Version = saved.Version;
        }
    }

    public void DeleteDeck(string id, long expectedVersion = 0)
    {
        lock (_gate)
        {
            var decks = Read(true);
            var existing = decks.FirstOrDefault(d => d.Id == id);
            if (existing == null) return;
            if (existing.Version != expectedVersion)
                throw new InvalidOperationException("牌組已更新，請重新載入後再刪除。");
            decks.Remove(existing);
            Write(decks);
        }
    }
}
