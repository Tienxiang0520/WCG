using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;

public class DiversityBalanceTests
{
    private static readonly string Root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb"));
    private static CardDatabase Cards()=>new(File.ReadAllText(Path.Combine(Root,"Data/cards.json")),File.ReadAllText(Path.Combine(Root,"Data/preset_decks.json")));
    private sealed class MemoryStorage : IPlayerStorage
    {
        public object Gate { get; }=new();public Dictionary<string,string> Values=new();public int Writes;
        public string? Read(string key)=>Values.GetValueOrDefault(key);
        public void Write(string key,string value,string? expected){Values[key]=value;Writes++;}
    }
    [Fact] public void NumericRebalanceLeavesCardEffectsAndKeywordsUnchanged()
    {
        var cards=Cards();var before=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(Path.Combine(Root,"Data/balance-before-diversity.json")))!;
        Assert.Equal(32,before.Count);
        foreach(var old in before){var current=cards.GetCard(old.Id)!;Assert.Equal(old.Text,current.Text);Assert.Equal(old.Keywords,current.Keywords);Assert.Equal(old.Arrows,current.Arrows);
            Assert.Equal(old.Type,current.Type);Assert.Equal(old.Will,current.Will);Assert.True(old.PP!=current.PP||old.DP!=current.DP||old.TotalCost!=current.TotalCost);Assert.Equal(current.TotalCost,current.CostGen);}
    }
    [Fact] public void ReferenceDecksArePlayableCopiesAndDoNotWritePlayerData()
    {
        var cards=Cards();var ranked=new RankedDecks(cards,File.ReadAllText(Path.Combine(Root,"Data/ranked_decks.json")));var storage=new MemoryStorage();
        var decks=new DeckService(cards,storage,ranked);var references=decks.GetReferenceDecks();Assert.Equal(30,references.Count);Assert.Equal(35,decks.GetAllAvailableDecks().Count);
        foreach(var d in references){Assert.True(ranked.IsLegal(d,out var error),error);Assert.NotNull(decks.GetDeck(d.Id));}
        references[0].CardIds.Clear();Assert.Equal(50,decks.GetReferenceDecks()[0].CardIds.Count);Assert.Equal(0,storage.Writes);
        var reference=decks.GetReferenceDecks()[0];Assert.Throws<ArgumentException>(()=>decks.SaveDeck(reference));Assert.Equal(0,storage.Writes);
        reference.Id=Guid.NewGuid().ToString();decks.SaveDeck(reference);Assert.Equal(1,storage.Writes);Assert.Single(decks.GetCustomDecks());Assert.Equal(30,decks.GetReferenceDecks().Count);
    }

    [Fact] public void LookupReturnsIndependentCopiesAndKeepsCustomDeckPriority()
    {
        var cards = Cards();
        var ranked = new RankedDecks(cards, File.ReadAllText(Path.Combine(Root, "Data/ranked_decks.json")));
        var storage = new MemoryStorage();
        var reference = ranked.Pool(5)[0].Deck;
        var custom = DeckService.Copy(reference);
        custom.Name = "玩家自己的名字";
        // Existing saves can contain this ID. Lookup must retain its previous custom-first ordering.
        storage.Values["decks"] = JsonSerializer.Serialize(new[] { custom });
        var decks = new DeckService(cards, storage, ranked);

        foreach (var source in new[] { custom, cards.PresetDecks[0], ranked.Pool(5)[1].Deck })
        {
            var copy = decks.GetDeck(source.Id)!;
            Assert.Equal(source.Name, copy.Name);
            copy.CardIds.Clear();
            copy.Name = "暫時修改";
            Assert.Equal(50, decks.GetDeck(source.Id)!.CardIds.Count);
            Assert.Equal(source.Name, decks.GetDeck(source.Id)!.Name);
        }
        Assert.Null(decks.GetDeck("missing-deck"));
        Assert.Equal(0, storage.Writes);
        Assert.Equal(50, reference.CardIds.Count);
    }
}
