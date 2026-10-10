using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;

public class StarterDeckTests
{
    private static CardDatabase Cards()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/Data"));
        return new(File.ReadAllText(Path.Combine(root, "cards.json")), File.ReadAllText(Path.Combine(root, "preset_decks.json")));
    }
    private sealed class Storage : IPlayerStorage
    {
        public object Gate { get; } = new();
        public string? Data;
        public int Writes;
        public bool Fail;
        public string? Read(string key) => Data;
        public void Write(string key, string value, string? expected)
        {
            if (Fail || Data != expected) throw new IOException("write rejected");
            Data = value; Writes++;
        }
    }
    [Fact] public void FreshPlayerGetsOneEditableLegalStarterOnlyOnce()
    {
        var cards = Cards(); var storage = new Storage(); var service = new DeckService(cards, storage);
        var starter = Assert.Single(service.GetPlayerDecks());
        Assert.True(starter.IsValid(cards.GetCard, out var error), error);
        Assert.Equal(50, starter.CardIds.Count);
        Assert.DoesNotContain(starter.CardIds, id => cards.GetCard(id)!.IsEnchantment || cards.GetCard(id)!.Arrows.Length > 0);
        Assert.DoesNotContain(cards.PresetDecks, d => d.Id == starter.Id);
        starter.Name = "edited"; service.SaveDeck(starter);
        var data = storage.Data;
        var next = Assert.Single(new DeckService(cards, storage).GetPlayerDecks());
        Assert.Equal(starter.Id, next.Id); Assert.Equal("edited", next.Name);
        next.CardIds.Clear(); Assert.Equal(50, service.GetPlayerDecks()[0].CardIds.Count);
        Assert.Equal(data, storage.Data); Assert.Equal(2, storage.Writes);
        service.DeleteDeck(starter.Id, starter.Version);
        Assert.Empty(service.GetPlayerDecks()); Assert.Equal(3, storage.Writes);
    }
    [Theory]
    [InlineData("[]")]
    [InlineData("broken json")]
    public void ExistingEmptyOrBrokenSaveIsNeverOverwritten(string data)
    {
        var storage = new Storage { Data = data }; var service = new DeckService(Cards(), storage);
        Assert.Empty(service.GetPlayerDecks()); Assert.Equal(data, storage.Data); Assert.Equal(0, storage.Writes);
        if (data != "[]") Assert.NotNull(service.LastStorageError);
    }
    [Fact] public void ExistingPlayerDecksRemainByteForByteIntact()
    {
        var deck = DeckService.CreateStarterDeck(); deck.Name = "existing custom"; deck.Version = 7;
        var storage = new Storage { Data = JsonSerializer.Serialize(new[] { deck }) }; var before = storage.Data;
        var result = Assert.Single(new DeckService(Cards(), storage).GetPlayerDecks());
        Assert.Equal(deck.Id, result.Id); Assert.Equal(7, result.Version); Assert.Equal(deck.CardIds, result.CardIds);
        Assert.Equal(before, storage.Data); Assert.Equal(0, storage.Writes);
    }
    [Fact] public void FailedFirstWriteReturnsNoUnsavedPlayableDeck()
    {
        var storage = new Storage { Fail = true }; var service = new DeckService(Cards(), storage);
        Assert.Empty(service.GetPlayerDecks()); Assert.NotNull(service.LastStorageError); Assert.Null(storage.Data);
        storage.Fail = false;
        Assert.Single(service.GetPlayerDecks()); Assert.Equal(1, storage.Writes);
    }
    [Fact] public void ServicesSharingStorageCannotCreateDuplicateStarters()
    {
        var cards = Cards(); var storage = new Storage();
        Parallel.For(0, 8, _ => Assert.Single(new DeckService(cards, storage).GetPlayerDecks()));
        Assert.Equal(1, storage.Writes);
    }
}
