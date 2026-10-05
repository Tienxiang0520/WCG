using System.Text.Json;
using LcgWeb.Models;

namespace LcgWeb.Services;

public partial class CardDatabase
{
    private readonly List<CardDefinition> _cards = new();
    private readonly Dictionary<string, CardDefinition> _cardLookup = new();
    private readonly List<Deck> _presetDecks = new();

    public IReadOnlyList<CardDefinition> AllCards => _cards;
    public IReadOnlyList<Deck> PresetDecks => _presetDecks;

    public CardDatabase(string cardsJson, string presetsJson)
    {
        var cards = JsonSerializer.Deserialize<List<CardDefinition>>(cardsJson) ?? throw new InvalidDataException("卡牌資料為空。");
        foreach (var card in cards)
        {
            if (string.IsNullOrWhiteSpace(card.Id) || !_cardLookup.TryAdd(card.Id, card))
                throw new InvalidDataException("卡牌 ID 不正確或重複。");
            _cards.Add(card);
        }
        InitializePresetDecks(presetsJson);
    }

    public CardDefinition? GetCard(string id)
    {
        return _cardLookup.GetValueOrDefault(id);
    }

    private void InitializePresetDecks(string json)
    {
        var decks = JsonSerializer.Deserialize<List<Deck>>(json) ?? throw new InvalidOperationException("預組資料為空。");
        if (decks.Count != 5 || decks.Select(d => d.Id).Distinct().Count() != 5) throw new InvalidOperationException("預組必須包含 5 副不同牌組。");
        foreach (var deck in decks)
        {
            if (!deck.IsValid(GetCard, out var error)) throw new InvalidOperationException($"預組 {deck.Name} 不合法：{error}");
            _presetDecks.Add(deck);
        }
    }
}
