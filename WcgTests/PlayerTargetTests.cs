using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Models.Battle;
using WcgWeb.Services;

namespace WcgTests;

public class PlayerTargetTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/Data"));
    private static CardDatabase Cards(bool old = false)
    {
        var cards = JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(Path.Combine(Root, "cards.json")))!;
        if (old)
        {
            var prior = JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(Path.Combine(Root, "balance-before-player-targets.json")))!.ToDictionary(c => c.Id);
            cards = cards.Select(c => prior.GetValueOrDefault(c.Id, c)).ToList();
        }
        return new(JsonSerializer.Serialize(cards), File.ReadAllText(Path.Combine(Root, "preset_decks.json")));
    }
    private sealed class Storage : IPlayerStorage
    {
        public object Gate { get; } = new();
        public string? Read(string key) => null;
        public void Write(string key, string value, string? expected) => throw new InvalidOperationException("No storage needed");
    }
    private static (GameEngine Game, BattleBridge Bridge, CardInstance Card) Scene(string id, bool old = false, bool computerTurn = false)
    {
        var cards = Cards(old); var game = new GameEngine(cards, new Random(19));
        game.StartGame(cards.PresetDecks[0], cards.PresetDecks[1], !computerTurn);
        game.Player.Hp = 2; game.Computer.Hp = 3;
        var actor = computerTurn ? game.Computer : game.Player;
        for (var i = 0; i < 12; i++) actor.EnergyZone.Add(new(cards.GetCard("WCG-101")!));
        var card = new CardInstance(cards.GetCard(id)!); actor.Hand.Add(card);
        return (game, new(game, new DeckService(cards, new Storage())), card);
    }
    private static BattleCommand Command(GameEngine g, string type, Guid? source = null, Guid? target = null) =>
        new(Guid.NewGuid(), g.MatchId, g.Revision, type, source, target);
    [Theory]
    [InlineData("WCG-050", false, 2)] [InlineData("WCG-050", true, 2)]
    [InlineData("WCG-066", false, 2)] [InlineData("WCG-066", true, 2)]
    [InlineData("WCG-070", false, 1)] [InlineData("WCG-070", true, 1)]
    public void DirectHealingUsesChosenPlayerAndOnlyCasterDraws(string id, bool enemy, int amount)
    {
        var (g, bridge, card) = Scene(id); var target = enemy ? g.Computer : g.Player;
        var hand = bridge.Snapshot().Hand.Single(h => h.Card.InstanceId == card.InstanceId);
        Assert.Equal("target", hand.Preparation);
        Assert.Equal(new[] { GameEngine.PlayerTargetId.ToString(), GameEngine.ComputerTargetId.ToString() }, hand.PlayTargets);
        var playerHand = g.Player.Hand.Count; var enemyHand = g.Computer.Hand.Count;
        var cmd = Command(g, "play", card.InstanceId, g.TargetId(target));
        Assert.True(bridge.Submit(cmd).Success);
        Assert.Equal(enemy ? 2 : 2 + amount, g.Player.Hp);
        Assert.Equal(enemy ? 3 + amount : 3, g.Computer.Hp);
        Assert.Equal(playerHand - 1 + (id == "WCG-066" ? 1 : 0), g.Player.Hand.Count);
        Assert.Equal(enemyHand, g.Computer.Hand.Count);
        Assert.Contains(card, g.Player.Graveyard);
        var hp = target.Hp; Assert.True(bridge.Submit(cmd).Success); Assert.Equal(hp, target.Hp);
    }
    [Fact] public void PendingHealingOnlyOffersHeroesAndCancelNeverPays()
    {
        var (g, bridge, card) = Scene("WCG-050"); var monster = new MonsterInstance(g.Player.Deck.First(c => c.Card.IsMonster).Card);
        g.Player.Field.Add(monster);
        Assert.True(bridge.Submit(Command(g, "play", card.InstanceId)).Success);
        Assert.Equal(new[] { GameEngine.PlayerTargetId.ToString(), GameEngine.ComputerTargetId.ToString() }, bridge.Snapshot().Pending!.Targets);
        Assert.False(bridge.Submit(Command(g, "target", target: monster.InstanceId)).Success);
        Assert.False(g.SelectPlayerTarget(new PlayerState { Id = "player" }));
        Assert.Contains(card, g.Player.Hand); Assert.Equal(12, g.Player.AvailableEnergy);
        Assert.True(bridge.Submit(Command(g, "cancel")).Success);
        Assert.Equal(2, g.Player.Hp); Assert.Equal(3, g.Computer.Hp);
        Assert.True(bridge.Submit(Command(g, "play", card.InstanceId)).Success);
        Assert.True(bridge.Submit(Command(g, "target", target: GameEngine.ComputerTargetId)).Success);
        Assert.Equal(2, g.Player.Hp); Assert.Equal(5, g.Computer.Hp);
    }
    [Fact] public void MonsterSpellsRejectHeroAndHealingRejectsMonsterWithoutSpending()
    {
        var (g, bridge, card) = Scene("WCG-026");
        g.Computer.Field.Add(new(g.Player.Deck.First(c => c.Card.IsMonster).Card));
        Assert.True(g.CanPlayCard(g.Player, card));
        Assert.False(bridge.Submit(Command(g, "play", card.InstanceId, GameEngine.PlayerTargetId)).Success);
        Assert.Contains(card, g.Player.Hand); Assert.Equal(12, g.Player.AvailableEnergy);
        var scene = Scene("WCG-050");
        var unit = new MonsterInstance(scene.Game.Player.Deck.First(c => c.Card.IsMonster).Card); scene.Game.Player.Field.Add(unit);
        Assert.False(scene.Bridge.Submit(Command(scene.Game, "play", scene.Card.InstanceId, unit.InstanceId)).Success);
        Assert.Contains(scene.Card, scene.Game.Player.Hand); Assert.Equal(12, scene.Game.Player.AvailableEnergy);
    }
    [Fact] public void PlayerHealCannotBypassEnergyAndCapsAtSeven()
    {
        var (g, bridge, card) = Scene("WCG-050"); g.Player.EnergyZone.Clear();
        Assert.False(bridge.Submit(Command(g, "play", card.InstanceId, GameEngine.PlayerTargetId)).Success);
        Assert.Contains(card, g.Player.Hand);
        var ready = Scene("WCG-050"); ready.Game.Computer.Hp = 6;
        Assert.True(ready.Bridge.Submit(Command(ready.Game, "play", ready.Card.InstanceId, GameEngine.ComputerTargetId)).Success);
        Assert.Equal(7, ready.Game.Computer.Hp);
    }
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void AiHealingSelectsItsOwnHero(int tier)
    {
        var (g, _, card) = Scene("WCG-066", computerTurn: true); g.AiLevel = tier;
        Assert.True(g.CastSpell(g.Computer, card)); Assert.True(g.ExecuteAiStep());
        Assert.Equal(2, g.Player.Hp); Assert.Equal(5, g.Computer.Hp); Assert.False(g.IsWaiting);
    }
    [Fact] public void PriorCatalogFingerprintAndAutomaticSelfHealArePreserved()
    {
        var cards = Cards(true);
        Assert.Equal("88C0C870C9CC1E0CEA81399269174E23D1A1E7649EA41F6BB91DCAE57EABA187",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cards.AllCards)))));
        var (g, bridge, card) = Scene("WCG-050", old: true);
        Assert.Empty(bridge.Snapshot().Hand.Single(h => h.Card.InstanceId == card.InstanceId).PlayTargets);
        Assert.True(g.CastSpell(g.Player, card)); Assert.Equal(4, g.Player.Hp); Assert.Equal(3, g.Computer.Hp); Assert.False(g.IsWaiting);
    }
    [Theory]
    [InlineData("WCG-050", "Restore 2 life.")]
    [InlineData("WCG-066", "Restore 2 life, then draw 1 card.")]
    [InlineData("WCG-070", "Restore 1 life.")]
    public void HistoricEnglishTextDoesNotPromiseNewPlayerTargeting(string id, string oldEnglish)
    {
        Assert.Equal(oldEnglish, LocalizationCatalog.CardText(id, Cards(true).GetCard(id)!.Text));
        Assert.Contains("either player", LocalizationCatalog.CardText(id, Cards().GetCard(id)!.Text));
    }
}
