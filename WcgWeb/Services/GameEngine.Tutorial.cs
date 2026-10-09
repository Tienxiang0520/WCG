using WcgWeb.Models;
using WcgWeb.Services.Tutorial;

namespace WcgWeb.Services;

// Tutorial lessons start from a fixed, hand-made board. Loading one only builds the starting state;
// every later action goes through the same rules as a normal match.
public partial class GameEngine
{
    internal bool LoadTutorialScene(TutorialScene scene) => Change(() =>
    {
        if (!TrainingMode) return Fail("教學場面只能在練習引擎中載入。");
        ClearMatch();
        Player = BuildTutorialSide(scene.Player, "player", "玩家");
        Computer = BuildTutorialSide(scene.Computer, "computer", "電腦");
        Player.IsAi = false; Computer.IsAi = true;
        CurrentTurnPlayerId = scene.ComputerActive ? "computer" : "player";
        TurnNumber = scene.Turn; CurrentPhase = TurnPhase.MainPhase;
        _random = new Random(scene.Seed); _aiSeed = scene.Seed;
        Log("教學場面已就緒。", "action");
        return true;
    });

    private PlayerState BuildTutorialSide(TutorialSide side, string id, string name)
    {
        CardDefinition Card(string cardId) => _cardDb.GetCard(cardId) ?? throw new InvalidOperationException($"教學場面使用了不存在的卡片 {cardId}。");
        var state = new PlayerState { Id = id, Name = name, Hp = side.Hp, HasFilledEnergyThisTurn = side.FilledEnergy };
        state.Hand.AddRange(side.Hand.Select(c => new CardInstance(Card(c))));
        state.Deck.AddRange(side.Deck.Select(c => new CardInstance(Card(c))));
        state.Graveyard.AddRange(side.Graveyard.Select(c => new CardInstance(Card(c))));
        for (var i = 0; i < side.Energy; i++) state.EnergyZone.Add(new(Card("WCG-101")) { IsTapped = i >= side.Energy - side.SpentEnergy });
        foreach (var unit in side.Board)
        {
            var card = Card(unit.CardId);
            var m = new MonsterInstance(card) { Slot = unit.Slot, IsSet = unit.Set, IsTapped = unit.Tapped, HasSummoningSickness = false };
            if (m.IsUnit) state.Field.Add(m); else state.Structures.Add(m);
        }
        return state;
    }
}
