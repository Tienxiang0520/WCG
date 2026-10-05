using System.Text.Json;
using WcgWeb.Models;
namespace WcgWeb.Services;

public partial class DeckService
{
    private readonly CardDatabase _cardDb;
    private readonly IPlayerStorage _storage;
    private readonly object _gate;
    private string? lastRead;
    public string? LastStorageError { get; private set; }
    public DeckService(CardDatabase cardDb, IPlayerStorage storage)
    { _cardDb = cardDb; _storage = storage; _gate = storage.Gate; }
    private List<Deck> Read(bool strict = false)
    {
        try
        {
            LastStorageError = null;
            lastRead = _storage.Read("decks");
            if (lastRead is null) return new();
            var decks = JsonSerializer.Deserialize<List<Deck>>(lastRead) ?? throw new JsonException("牌組檔內容為空。");
            if (decks.Any(d => d == null || d.CardIds == null || d.Version < 0 || string.IsNullOrWhiteSpace(d.Id)) || decks.Select(d => d.Id).Distinct().Count() != decks.Count)
                throw new JsonException("牌組資料結構或 ID 不正確。");
            return decks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LastStorageError = $"牌組存檔讀取失敗，原資料保留：{ex.Message}";
            if (strict) throw new InvalidOperationException(LastStorageError, ex);
            return new();
        }
    }
    private void Write(List<Deck> decks)
    {
        var json = JsonSerializer.Serialize(decks, new JsonSerializerOptions { WriteIndented = true });
        _storage.Write("decks", json, lastRead);
        lastRead = json;
        LastStorageError = null;
    }
    public static Deck Copy(Deck d) => new() { Id = d.Id, Name = d.Name, Description = d.Description, MainWill = d.MainWill, Version = d.Version, CardIds = new(d.CardIds) };
    public List<Deck> GetCustomDecks() { lock (_gate) return Read().Select(Copy).ToList(); }
    public List<Deck> GetAllAvailableDecks() => GetCustomDecks().Concat(_cardDb.PresetDecks.Select(Copy)).ToList();
    public Deck? GetDeck(string id) => GetCustomDecks().FirstOrDefault(d => d.Id == id) ?? _cardDb.PresetDecks.Where(d => d.Id == id).Select(Copy).FirstOrDefault();
    public void SaveDeck(Deck deck)
    {
        if (string.IsNullOrWhiteSpace(deck.Id) || deck.Version < 0) throw new ArgumentException("牌組 ID 或版本不正確。");
        if (!deck.IsValid(_cardDb.GetCard, out var error)) throw new ArgumentException(error);
        if (_cardDb.PresetDecks.Any(d => d.Id == deck.Id)) throw new ArgumentException("請複製預設牌組後另存。");
        lock (_gate)
        {
            var decks = Read(true); var index = decks.FindIndex(d => d.Id == deck.Id);
            if (index >= 0 && decks[index].Version != deck.Version || index < 0 && deck.Version != 0)
                throw new InvalidOperationException("牌組已由其他頁面更新或刪除，請重新載入後再編輯。");
            var saved = Copy(deck); saved.Version = deck.Version + 1;
            if (index >= 0) decks[index] = saved; else decks.Add(saved);
            Write(decks); deck.Version = saved.Version;
        }
    }
    public void DeleteDeck(string id, long expectedVersion = 0)
    {
        lock (_gate)
        {
            var decks = Read(true); var existing = decks.FirstOrDefault(d => d.Id == id);
            if (existing == null) return;
            if (existing.Version != expectedVersion) throw new InvalidOperationException("牌組已更新，請重新載入後再刪除。");
            decks.Remove(existing); Write(decks);
        }
    }
}
