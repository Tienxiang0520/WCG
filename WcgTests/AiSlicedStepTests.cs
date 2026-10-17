using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Models.Battle;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
namespace WcgTests;

// The browser runs the computer's step in slices (GameEngine.BeginAiStep) so the page keeps rendering while it
// thinks. A sliced step must make exactly the decisions ExecuteAiStep makes: same moves, same state, same journal.
public sealed class AiSlicedStepTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "wcg-sliced-test-" + Guid.NewGuid());
    readonly IWebHostEnvironment env;
    readonly CardDatabase cards;
    readonly RankedDecks decks;
    readonly RankedStore store;
    public AiSlicedStepTests()
    {
        var mock = new Mock<IWebHostEnvironment>(); mock.Setup(x => x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb")));
        env = mock.Object; cards = new(env); decks = new(cards, env);
        store = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ranked:SavePath"] = Path.Combine(dir, "ranked.json") }).Build());
    }
    public void Dispose() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }

    static string Shape(GameEngine e) => JsonSerializer.Serialize(new
    {
        e.Revision, e.TurnNumber, e.CurrentTurnPlayerId, e.CurrentPhase, e.DecisionPlayerId, e.IsOver, e.LastError,
        sides = new[] { e.Player, e.Computer }.Select(p => new
        {
            p.Hp, p.HasLost, p.HasFilledEnergyThisTurn, p.AvailableEnergy, p.TotalEnergy,
            hand = p.Hand.Select(c => c.Card.Id), deck = p.Deck.Select(c => c.Card.Id), grave = p.Graveyard.Select(c => c.Card.Id),
            energy = p.EnergyZone.Select(c => new { c.Card.Id, c.IsTapped }),
            field = p.Field.Select(m => new { m.Card.Id, m.Slot, m.CurrentPP, m.CurrentDP, m.IsTapped, m.HasAttacked, m.IsSilenced, m.ShieldCounters, m.ShieldCount, a = m.Attachments.Select(x => x.Card.Card.Id) }),
            structures = p.Structures.Select(m => new { m.Card.Id, m.IsSet, m.IsTapped }),
        }),
        logs = e.Logs.Select(l => l.Message),
        events = e.PresentationEvents.Length,
        choice = e.CurrentPendingChoice?.Options.Select(o => o.Title), target = e.CurrentPendingTarget?.Title,
    });

    GameEngine NewGame(int tier, int seed, int revision)
    {
        var e = new GameEngine(cards, new Random(900 + seed)); e.SeedRankedAi(900 + seed);
        var tierDecks = decks.All.Where(x => x.Tier == tier).ToList();
        e.StartGame(tierDecks[seed % tierDecks.Count].Deck, cards.PresetDecks[seed % cards.PresetDecks.Count], seed % 2 == 0);
        e.Player.IsAi = true; e.AiLevel = tier; e.AiRevision = revision; return e;
    }

    // Plays the same game twice, once with ExecuteAiStep and once in slices, comparing the whole state after every step.
    (int steps, int maxSlices) Compare(int tier, int seed, int revision, int maxSteps)
    {
        var whole = NewGame(tier, seed, revision); var sliced = NewGame(tier, seed, revision);
        Assert.Equal(Shape(whole), Shape(sliced));
        int steps = 0, maxSlices = 0;
        for (; steps < maxSteps && !whole.IsOver; steps++)
        {
            var expected = whole.ExecuteAiStep();
            var run = sliced.BeginAiStep(); int slices = 1; var revision0 = sliced.Revision;
            while (!run.Advance()) { slices++; Assert.Equal(revision0, sliced.Revision); } // nothing is published mid-step
            maxSlices = Math.Max(maxSlices, slices);
            Assert.Equal(expected, run.Result);
            Assert.True(expected, whole.LastError);
            Assert.Equal(Shape(whole), Shape(sliced));
        }
        return (steps, maxSlices);
    }

    [Fact]
    public void SlicedMasterStepsMatchTheWholeStep()
    {
        var (steps, maxSlices) = Compare(5, 1, GameEngine.RankedAiVersion, 60);
        Assert.True(steps > 30);
        Assert.True(maxSlices > 50, $"a Master search should pause many times, saw {maxSlices}");
    }

    [Theory, Trait("Category", "Slow")]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void SlicedStepsMatchWholeGamesAtEveryTier(int tier)
    {
        foreach (var seed in new[] { 0, 3, 8 })
        {
            var (steps, _) = Compare(tier, seed, GameEngine.RankedAiVersion, 4000);
            Assert.True(steps > 20);
        }
        // Old journals replay with their recorded AI version; v7 steps take the same sliced path.
        Compare(tier, 5, 7, 4000);
    }

    [Fact]
    public void PracticeAndTieredAiStepsAreASingleSlice()
    {
        foreach (var level in new[] { -1, 3 })
        {
            var whole = NewGame(3, 2, 6); var sliced = NewGame(3, 2, 6);
            whole.AiLevel = sliced.AiLevel = level;
            for (int i = 0; i < 80 && !whole.IsOver; i++)
            {
                var expected = whole.ExecuteAiStep(); var run = sliced.BeginAiStep();
                Assert.True(run.Advance()); Assert.Equal(expected, run.Result); Assert.Equal(Shape(whole), Shape(sliced));
            }
        }
    }

    [Fact]
    public void StateChangedFiresOnceAfterASlicedStep()
    {
        var e = NewGame(5, 4, GameEngine.RankedAiVersion); int fired = 0; e.OnStateChanged += () => fired++;
        for (int i = 0; i < 30 && !e.IsOver; i++)
        {
            var run = e.BeginAiStep(); var before = fired;
            while (!run.Advance()) Assert.Equal(before, fired);
            Assert.Equal(before + 1, fired);
        }
    }

    RankedSession Session() => new(cards, env, store, decks, NullLogger<BattleCoordinator>.Instance, TimeProvider.System);

    // A Master ranked match driven by the coordinator, pausing after every slice. Restoring the save replays every
    // computer step with the whole-step path and must reach the same position.
    [Fact, Trait("Category", "Slow")]
    public async Task SlicedRankedMatchRestoresToTheSamePosition()
    {
        var s = Session(); Assert.True(s.Start(cards.PresetDecks[1]).Success);
        var profile = s.Read(); profile.Match!.Seed = 4242; profile.Match.Tier = 5; profile.Match.PlayerFirst = false;
        profile.Match.ComputerDeck = DeckService.Copy(decks.All.First(x => x.Tier == 5).Deck); store.Save(profile); s = Session();
        Assert.Equal(5, s.Engine.AiLevel);
        var slice = BattleCoordinator.SliceMilliseconds; BattleCoordinator.SliceMilliseconds = 2;
        try
        {
            for (int n = 0; n < 600 && !s.Engine.IsOver; n++)
            {
                var state = s.Bridge.Snapshot();
                if (state.DecisionPlayerId == "computer")
                {
                    var token = s.Coordinator.Attach(); s.Coordinator.Fast = true; using var cancel = new CancellationTokenSource();
                    await s.Coordinator.DriveAsync(token, r => { Assert.True(r.Success, r.Message); cancel.Cancel(); return Task.CompletedTask; }, cancel.Token);
                    s.Coordinator.Detach(token);
                    continue;
                }
                BattleResponse response;
                if (state.Pending is { } pending) response = Send(s, pending.Kind, option: pending.Kind == "choice" ? pending.Options[0].Id : null, target: pending.Kind == "target" ? Guid.Parse(pending.Targets[0]) : null);
                else if (state.Hand.FirstOrDefault(h => h.CanEnergy && state.Player.TotalEnergy < 5) is { } energy) response = Send(s, "energy", energy.Card.InstanceId);
                else if (state.Hand.FirstOrDefault(h => h.CanPlay) is { } card) response = Send(s, "play", card.Card.InstanceId);
                else if (state.Player.Field.FirstOrDefault(m => m.CanAttack && m.Targets.Length > 0) is { } monster)
                { var t = monster.Targets.FirstOrDefault(x => x.Id == "face") ?? monster.Targets[0]; response = Send(s, "attack", monster.Card.InstanceId, t.Id == "face" ? null : Guid.Parse(t.Id)); }
                else response = Send(s, "end");
                Assert.True(response.Success, response.Message);
            }
        }
        finally { BattleCoordinator.SliceMilliseconds = slice; }
        Assert.True(s.Engine.IsOver);
        var restored = Session(); Assert.Empty(restored.Error);
        Assert.Equal(Shape(s.Engine).Split("\"logs\"")[0], Shape(restored.Engine).Split("\"logs\"")[0]);
    }

    BattleResponse Send(RankedSession s, string type, Guid? id = null, Guid? target = null, string? option = null)
    { var state = s.Bridge.Snapshot(); return s.Bridge.Submit(new(Guid.NewGuid(), state.MatchId, state.Revision, type, id, target, option)); }

    // While the computer thinks, the player's commands are refused and the page can still read the board.
    [Fact]
    public async Task CommandsWaitWhileTheComputerThinks()
    {
        var s = Session(); Assert.True(s.Start(cards.PresetDecks[0]).Success);
        var profile = s.Read(); profile.Match!.Seed = 77; profile.Match.Tier = 5; profile.Match.PlayerFirst = false;
        profile.Match.ComputerDeck = DeckService.Copy(decks.All.First(x => x.Tier == 5).Deck); store.Save(profile); s = Session();
        Assert.Equal("computer", s.Engine.DecisionPlayerId);
        int pauses = 0, steps = 0;
        while (pauses == 0 && s.Engine.DecisionPlayerId == "computer" && steps < 20)
        {
            var before = s.Bridge.Snapshot();
            var response = await s.Bridge.AiStepAsync(() =>
            {
                pauses++;
                var now = s.Bridge.Snapshot(); Assert.Equal(before.Revision, now.Revision);
                Assert.False(s.Bridge.Submit(new(Guid.NewGuid(), now.MatchId, now.Revision, "surrender")).Success);
                Assert.Equal(before.Revision, s.Engine.Revision);
                return ValueTask.CompletedTask;
            });
            Assert.True(response.Success, response.Message); steps++;
            Assert.Equal(steps, s.Read().Match!.Actions.Count);
        }
        Assert.True(pauses > 0);
        Assert.False(s.Engine.IsOver);
    }
}
