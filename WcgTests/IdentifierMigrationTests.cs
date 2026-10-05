using System.Text.Json;
using System.Text.Json.Nodes;
using WcgWeb.Models;
using WcgWeb.Models.Ranked;
using WcgWeb.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace WcgTests;

public sealed class IdentifierMigrationTests
{
    readonly string data = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/Data"));
    readonly string fixtures = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgTests/Fixtures"));
    CardDatabase Cards() => new(File.ReadAllText(Path.Combine(data,"cards.json")),File.ReadAllText(Path.Combine(data,"preset_decks.json")));
    string Fixture(string name) => File.ReadAllText(Path.Combine(fixtures,name));
    RankedSession Session(CardDatabase cards, MemoryStorage storage) => new(cards,new DeckService(cards,storage),new RankedStore(storage),
        new RankedDecks(cards,File.ReadAllText(Path.Combine(data,"ranked_decks.json"))),NullLogger<BattleCoordinator>.Instance,new FixedClock());

    [Fact] public void EveryLegacyIdentifierResolvesToItsCanonicalCard()
    {
        var cards=Cards();
        Assert.All(cards.AllCards,card=>{
            Assert.StartsWith(CardIdentifier.CurrentPrefix,card.Id);
            Assert.Same(card,cards.GetCard(CardIdentifier.LegacyPrefix+card.Id[CardIdentifier.CurrentPrefix.Length..]));
        });
        Assert.Null(cards.GetCard(CardIdentifier.LegacyPrefix+"999"));
    }

    [Fact] public void OriginalDeckSaveReadsCanonicallyWithoutRewritingUntilSave()
    {
        var raw=Fixture("legacy-decks.json");var storage=new MemoryStorage();storage.Values["decks"]=raw;
        var service=new DeckService(Cards(),storage);var deck=Assert.Single(service.GetCustomDecks());
        Assert.All(deck.CardIds,id=>Assert.StartsWith(CardIdentifier.CurrentPrefix,id));
        Assert.Equal(raw,storage.Read("decks"));Assert.Equal(2,deck.Version);
        service.SaveDeck(deck);
        Assert.DoesNotContain(CardIdentifier.LegacyPrefix,storage.Read("decks")!);
        Assert.Equal(3,deck.Version);
    }

    [Fact] public void MixingIdentifierAliasesCannotBypassTheFourCopyLimit()
    {
        var cards=Cards();var deck=DeckService.Copy(cards.PresetDecks[0]);
        deck.CardIds[^1]=CardIdentifier.LegacyPrefix+deck.CardIds[0][CardIdentifier.CurrentPrefix.Length..];
        Assert.False(deck.IsValid(cards.GetCard,out var error));Assert.Contains("超過 4",error);
    }

    [Fact] public void OriginalActiveRankedJournalReplaysTheExactPreRenameState()
    {
        var raw=Fixture("legacy-ranked.json");var storage=new MemoryStorage();storage.Values["ranked"]=raw;
        var session=Session(Cards(),storage);
        try {
            Assert.Empty(session.Error);
            Assert.Equal(Fixture("legacy-rule-fingerprint.txt"),session.Read().Match!.Rules);
            Assert.Equal(2,session.Read().Match!.Actions.Count);
            Assert.Equal(Fixture("legacy-state.json").Replace(CardIdentifier.LegacyPrefix,CardIdentifier.CurrentPrefix),StateShape(session.Engine));
            Assert.Equal(raw,storage.Read("ranked"));
        } finally {session.Coordinator.Dispose();}
    }

    [Fact] public void IdentifierCompatibilityDoesNotAcceptDifferentCardBalance()
    {
        var catalog=JsonNode.Parse(File.ReadAllText(Path.Combine(data,"cards.json")))!.AsArray();
        catalog[0]!["pp"]=catalog[0]!["pp"]!.GetValue<int>()+100;
        var cards=new CardDatabase(catalog.ToJsonString(),File.ReadAllText(Path.Combine(data,"preset_decks.json")));
        var raw=Fixture("legacy-ranked.json");var storage=new MemoryStorage();storage.Values["ranked"]=raw;
        var session=Session(cards,storage);
        try {Assert.NotEmpty(session.Error);Assert.Equal(raw,storage.Read("ranked"));}
        finally {session.Coordinator.Dispose();}
    }

    [Fact] public void OriginalExportWithCustomDeckAndActiveMatchStillValidates()
    {
        var cards=Cards();var backup=JsonSerializer.Serialize(new{format="soul-oath-local",version=1,
            data=new Dictionary<string,string>{{"decks",Fixture("legacy-decks.json")},{"ranked",Fixture("legacy-ranked.json")}},preferences=new{}});
        PlayerBackupValidator.Validate(backup,cards,new RankedDecks(cards,File.ReadAllText(Path.Combine(data,"ranked_decks.json"))));
    }

    static string StateShape(GameEngine e)=>JsonSerializer.Serialize(new{e.TurnNumber,e.CurrentTurnPlayerId,e.CurrentPhase,e.DecisionPlayerId,
        sides=new[]{e.Player,e.Computer}.Select(p=>new{p.Hp,p.HasLost,p.HasFilledEnergyThisTurn,
            hand=p.Hand.Select(c=>c.Card.Id),deck=p.Deck.Select(c=>c.Card.Id),energy=p.EnergyZone.Select(c=>new{c.Card.Id,c.IsTapped}),
            field=p.Field.Select(m=>new{m.Card.Id,m.CurrentPP,m.CurrentDP,m.HasAttacked,m.HasSummoningSickness,m.IsFrozen,m.IsSilenced,m.IsStealthed,m.ShieldCount}),grave=p.Graveyard.Select(c=>c.Card.Id)})});
    sealed class MemoryStorage:IPlayerStorage
    {
        public object Gate{get;}=new();public Dictionary<string,string> Values{get;}=new();
        public string? Read(string key)=>Values.GetValueOrDefault(key);
        public void Write(string key,string value,string? expected)=>Values[key]=value;
    }
    sealed class FixedClock:TimeProvider {public override DateTimeOffset GetUtcNow()=>new(2026,10,5,0,0,0,TimeSpan.Zero);}
}
