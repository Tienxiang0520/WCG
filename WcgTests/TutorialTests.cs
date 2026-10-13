using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WcgWeb.Models;
using WcgWeb.Models.Battle;
using WcgWeb.Services;
using WcgWeb.Services.Tutorial;

namespace WcgTests;

public class TutorialTests
{
    readonly CardDatabase cards;
    readonly DeckService decks;
    public TutorialTests()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(x => x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb")));
        cards = new(env.Object); decks = new DeckService(cards, env.Object);
    }
    TutorialSession New() { var s = new TutorialSession(cards, decks, NullLogger<BattleCoordinator>.Instance); s.Coordinator.Fast = true; return s; }
    public static IEnumerable<object[]> BattleLessons => TutorialLessons.All.Where(l => l.IsBattle).Select(l => new object[] { l.Id });

    static string? CardOf(GameEngine e, Guid id) => new[] { e.Player, e.Computer }
        .SelectMany(p => p.Board.Select(m => (m.InstanceId, m.Card.Id)).Concat(p.Hand.Concat(p.Graveyard).Concat(p.Deck).Select(c => (c.InstanceId, c.Card.Id))))
        .FirstOrDefault(x => x.InstanceId == id).Id;
    static BattleCommand Cmd(GameEngine e, string type, Guid? id = null, Guid? target = null, string? option = null) =>
        new(Guid.NewGuid(), e.MatchId, e.Revision, type, id, target, option);

    // Builds the command a player following the coach panel would send.
    static BattleCommand? Solve(GameEngine e, TutorialStep step)
    {
        if (e.CurrentPendingChoice is { OwnerId: "player" } choice)
        {
            foreach (var a in step.Allow.Where(a => a.Type == "choice"))
            {
                var option = choice.Options.FirstOrDefault(o =>
                    a.Option is { } wanted ? (wanted.EndsWith('*') ? o.Id.StartsWith(wanted[..^1]) : o.Id == wanted)
                    : a.OptionCard is { } card && o.Id != "SKIP" && Guid.TryParse(o.Id, out var g) && (card == "*" || CardOf(e, g) == card));
                if (option != null) return Cmd(e, "choice", option: option.Id);
            }
            return null;
        }
        if (e.CurrentPendingTarget is { OwnerId: "player" } pending)
        {
            if (pending.PlayerValidator != null)
            {
                var allowed = step.Allow.FirstOrDefault(a => a.Type == "target" && a.TargetPlayer != null);
                var player = allowed?.TargetPlayer == "player" ? e.Player : e.Computer;
                return allowed != null && pending.PlayerValidator(player) ? Cmd(e, "target", target: e.TargetId(player)) : null;
            }
            var all = e.Player.Board.Concat(e.Computer.Board).Where(m => pending.Validator?.Invoke(m) != false).ToList();
            foreach (var a in step.Allow.Where(a => a.Type == "target"))
                if (all.FirstOrDefault(m => a.Target == null || m.Card.Id == a.Target) is { } m) return Cmd(e, "target", target: m.InstanceId);
            return null;
        }
        foreach (var a in step.Allow.Where(a => a.Type is not ("choice" or "target")))
        {
            Guid? Find(IEnumerable<(Guid Id, string Card)> pool, string? card) => pool.Where(x => card == null || x.Card == card).Select(x => (Guid?)x.Id).FirstOrDefault();
            var hand = e.Player.Hand.Select(c => (c.InstanceId, c.Card.Id));
            var own = e.Player.Board.Select(m => (m.InstanceId, m.Card.Id));
            var any = e.Player.Board.Concat(e.Computer.Board).Select(m => (m.InstanceId, m.Card.Id));
            var enemy = e.Computer.Board.Select(m => (m.InstanceId, m.Card.Id));
            switch (a.Type)
            {
                case "energy": case "set": return Cmd(e, a.Type, Find(hand, a.Card));
                case "play": return Cmd(e, "play", Find(hand, a.Card), a.TargetPlayer != null ? e.TargetId(a.TargetPlayer == "player" ? e.Player : e.Computer) : a.Target == null ? null : Find(any, a.Target));
                case "activate": return Cmd(e, "activate", Find(own, a.Card));
                case "attack": return Cmd(e, "attack", Find(own, a.Card), a.Face ? null : Find(enemy, a.Target));
                case "end": return Cmd(e, "end");
            }
        }
        return null;
    }

    static async Task DriveComputer(TutorialSession s, Guid token)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        s.Coordinator.Resync(token);
        await s.Coordinator.DriveAsync(token, r =>
        {
            s.Coordinator.Resync(token);
            if (s.Engine.DecisionPlayerId != "computer" || s.Engine.IsOver) stop.Cancel();
            return Task.CompletedTask;
        }, stop.Token);
        Assert.True(s.Engine.DecisionPlayerId != "computer" || s.Engine.IsOver, "電腦回合沒有結束");
    }

    static async Task Play(TutorialSession s, string id)
    {
        var token = s.Coordinator.Attach();
        for (var guard = 0; guard < 120 && !s.Completed; guard++)
        {
            var e = s.Engine; var step = s.Step!;
            if (step.IsInfo) { s.Next(); continue; }
            if (e.DecisionPlayerId == "computer" && !e.IsOver) { await DriveComputer(s, token); s.Evaluate(); continue; }
            Assert.False(step.IsWaiting, $"{id}#{s.StepIndex} {step.Title} 等待電腦，但輪到玩家");
            var command = Solve(e, step);
            Assert.True(command != null, $"{id}#{s.StepIndex} {step.Title} 找不到允許的操作");
            s.Coordinator.Resync(token);
            var response = s.Coordinator.Submit(token, command!);
            Assert.True(response.Success, $"{id}#{s.StepIndex} {step.Title}: {response.Message}");
        }
        Assert.True(s.Completed, $"{id} 未完成，停在第 {s.StepIndex + 1} 步");
        Assert.False(s.Engine.Player.HasLost);
    }

    [Theory, MemberData(nameof(BattleLessons))]
    public async Task EveryBattleLessonIsCompletableWithOnlyItsAllowedActions(string id)
    {
        using var s = New(); s.Load(TutorialLessons.Find(id)!);
        await Play(s, id);
    }

    [Theory, MemberData(nameof(BattleLessons))]
    public async Task RetryRestoresTheSameSceneAndCanBeFinishedAgain(string id)
    {
        using var s = New(); var lesson = TutorialLessons.Find(id)!;
        var first = s.Load(lesson).State; await Play(s, id);
        var again = s.Restart().State;
        Assert.False(s.Completed); Assert.Equal(0, s.StepIndex);
        string Shape(BattleSnapshot x) => JsonSerializer.Serialize(new
        {
            x.Turn, x.ActivePlayerId, P = x.Player.Hp, C = x.Computer.Hp, Hand = x.Hand.Select(h => h.Card.CardId), PE = x.Player.TotalEnergy, CE = x.Computer.TotalEnergy,
            PF = x.Player.Field.Select(m => (m.Card.CardId, m.Slot)), CF = x.Computer.Field.Select(m => (m.Card.CardId, m.Slot)), PD = x.Player.DeckCount, CD = x.Computer.DeckCount
        });
        Assert.Equal(Shape(first), Shape(again));
        await Play(s, id);
    }

    [Fact]
    public void ScenesUseRealCardsAndLegalSlots()
    {
        foreach (var lesson in TutorialLessons.All.Where(l => l.IsBattle))
        {
            foreach (var side in new[] { lesson.Scene!.Player, lesson.Scene.Computer })
            {
                Assert.All(side.Hand.Concat(side.Deck).Concat(side.Graveyard).Concat(side.Board.Select(b => b.CardId)), id => Assert.NotNull(cards.GetCard(id)));
                Assert.All(side.Board, b => Assert.InRange(b.Slot, 0, 4));
                Assert.Equal(side.Board.Length, side.Board.Select(b => b.Slot).Distinct().Count());
                Assert.InRange(side.Hp, 1, 7);
            }
            Assert.All(lesson.Steps.SelectMany(s => s.Allow).SelectMany(a => new[] { a.Card, a.Target, a.OptionCard }).Where(x => x is not (null or "*")), id => Assert.NotNull(cards.GetCard(id!)));
            Assert.True(lesson.Steps[^1].IsInfo, $"{lesson.Id} 應以說明步驟收尾");
            // Spotlight tokens are parsed by TutorialPage.Selectors; a slot token without a number crashes the circuit.
            Assert.All(lesson.Steps.SelectMany(s => s.Spot), t => Assert.Matches(@"^(face|hero|energy|end|history|hand(:WCG-\d{3})?|(own|enemy):[0-4]|(own|enemy)-card:WCG-\d{3}|dialog|field)$", t));
        }
        Assert.Equal(TutorialLessons.All.Count, TutorialLessons.All.Select(l => l.Id).Distinct().Count());
        Assert.Equal(TutorialLessons.BeginnerId, TutorialLessons.All[0].Id);
        Assert.All(TutorialLessons.All.Where(l => !l.IsBattle), l => Assert.NotEmpty(l.Tour));
    }

    [Fact]
    public void TutorialCardDescriptionsReadCurrentCatalogueDataInBothLanguages()
    {
        using var s = New(); var lesson = TutorialLessons.Find("traps")!; s.Load(lesson);
        using var language = new Localizer(new PlayerProfileStore(new MemoryStorage()));
        var step = lesson.Steps.Single(x => x.Title == "放置結界");
        Assert.Contains("費用 1", s.RenderText(step, language));
        var card = cards.GetCard("WCG-151")!;
        card.TotalCost = 5; card.Name = "測試鍛爐";
        Assert.Contains("「測試鍛爐」（費用 5）", s.RenderText(step, language));
        language.Set(Localizer.English);
        Assert.Contains(language.CardName(card), s.RenderText(step, language));
        Assert.Contains("cost 5", s.RenderText(step, language));

        var spellLesson = TutorialLessons.Find("spells")!; s.Load(spellLesson);
        cards.GetCard("WCG-004")!.Text = "測試效果：抽 3 張牌。";
        language.Set(Localizer.Chinese);
        Assert.Contains("測試效果：抽 3 張牌。", s.RenderText(spellLesson.Steps.Single(x => x.Title == "消滅並抽牌"), language));
    }

    [Fact]
    public void TutorialCombatTextReadsLivePowerAndDamageInsteadOfOldNumbers()
    {
        using var s = New(); var lesson = TutorialLessons.Find("status")!; s.Load(lesson);
        using var language = new Localizer(new PlayerProfileStore(new MemoryStorage()));
        var unit = s.Engine.Player.Field.Single(x => x.Card.Id == "WCG-154");
        unit.TurnBonus += 300;
        Assert.Equal(1800, unit.CurrentPP);
        Assert.Contains($"{unit.CurrentPP} PP", s.RenderText(lesson.Steps.Single(x => x.Title == "擊破"), language));
        var taunt = TutorialLessons.Find("taunt")!; s.Load(taunt);
        cards.GetCard("WCG-156")!.DP = 4;
        Assert.Contains("DP 是 4", s.RenderText(taunt.Steps.Single(x => x.Title == "打穿防線"), language));
        Assert.Contains("1600 PP", s.RenderText(taunt.Steps.Single(x => x.Title == "擊破嘲諷"), language));
    }

    [Fact]
    public void CounterLessonReportsEnergyActuallyRemainingAfterPlayingTheWard()
    {
        using var s = New(); var lesson = TutorialLessons.Find("traps")!; s.Load(lesson);
        using var language = new Localizer(new PlayerProfileStore(new MemoryStorage()));
        var token = s.Coordinator.Attach(); s.Next();
        var step = s.Step!;
        var command = Solve(s.Engine, step)!;
        s.Coordinator.Resync(token); Assert.True(s.Coordinator.Submit(token, command).Success);
        if (s.Engine.IsWaiting)
        {
            command = Cmd(s.Engine, "choice", option: "SLOT:2");
            s.Coordinator.Resync(token); Assert.True(s.Coordinator.Submit(token, command).Success);
        }
        Assert.Equal(5, s.Engine.Player.AvailableEnergy);
        Assert.Contains("還留著 5 點", s.RenderText(lesson.Steps.Single(x => x.Title == "結束回合"), language));
    }

    [Fact]
    public void EveryBoundDescriptionAndHintResolvesToLocalizedTextWithRealCardIds()
    {
        using var s = New();
        using var language = new Localizer(new PlayerProfileStore(new MemoryStorage()));
        foreach (var lesson in TutorialLessons.All.Where(x => x.IsBattle))
        {
            s.Load(lesson);
            foreach (var step in lesson.Steps)
            {
                foreach (var value in step.TextValues.Concat(step.HintValues).Where(x => x.CardId != ""))
                    Assert.NotNull(cards.GetCard(value.CardId));
                foreach (var lang in new[] { Localizer.Chinese, Localizer.English })
                {
                    language.Set(lang);
                    var texts = new[] { s.RenderText(step, language), TutorialText.Render(step.Hint, step.HintValues, cards, s.Engine, language) };
                    foreach (var text in texts)
                    {
                        Assert.DoesNotMatch(@"\{\d+\}", text);
                        if (lang == Localizer.English) Assert.False(LocalizationCatalog.HasHan(text), $"{lesson.Id}: {text}");
                    }
                }
            }
        }
    }

    [Fact]
    public void ActionsOutsideTheCurrentStepAreRejectedWithoutChangingTheBoard()
    {
        using var s = New(); s.Load(TutorialLessons.Find("basics")!); var token = s.Coordinator.Attach(); var e = s.Engine;
        // Info step: nothing may be played yet.
        var revision = e.Revision;
        var early = s.Coordinator.Submit(token, Cmd(e, "end"));
        Assert.False(early.Success); Assert.Equal(revision, e.Revision); Assert.Contains("下一步", early.Message);
        s.Next(); s.Next();
        Assert.Equal("填能量", s.Step!.Title);
        var wrong = s.Coordinator.Submit(token, Cmd(e, "energy", e.Player.Hand.First(c => c.Card.Id == "WCG-101").InstanceId));
        Assert.False(wrong.Success); Assert.Equal(revision, e.Revision); Assert.Contains("冒險者行囊", wrong.Message);
        Assert.Contains("冒險者行囊", s.Notice);
        var right = s.Coordinator.Submit(token, Cmd(e, "energy", e.Player.Hand.First(c => c.Card.Id == "WCG-121").InstanceId));
        Assert.True(right.Success); Assert.Equal("召喚怪物", s.Step!.Title); Assert.Equal("", s.Notice);
    }

    [Fact]
    public void TauntLessonOnlyAcceptsTheTaughtAttackAndTheEngineStillEnforcesTaunt()
    {
        using var s = New(); s.Load(TutorialLessons.Find("taunt")!); var token = s.Coordinator.Attach(); var e = s.Engine;
        s.Next();
        var giant = e.Player.Field.First(m => m.Card.Id == "WCG-114");
        Assert.False(s.Coordinator.Submit(token, Cmd(e, "attack", giant.InstanceId)).Success);
        // Normal rules are untouched: the same face attack is illegal for the engine itself while the taunt stands.
        Assert.False(e.Attack(e.Player, giant));
        Assert.Equal(4, e.Computer.Hp);
    }

    [Fact]
    public void SurrenderLosesTheLessonAndRetryStartsOver()
    {
        using var s = New(); s.Load(TutorialLessons.Find("combat")!); var token = s.Coordinator.Attach();
        Assert.True(s.Coordinator.Submit(token, Cmd(s.Engine, "surrender")).Success);
        Assert.True(s.Failed);
        s.Restart(); Assert.False(s.Failed); Assert.Equal(7, s.Engine.Player.Hp);
        Assert.False(s.Coordinator.Submit(token, Cmd(s.Engine, "reset")).Success);
    }

    sealed class MemoryStorage : IPlayerStorage
    {
        public Dictionary<string, string> Values { get; } = new();
        public object Gate { get; } = new();
        public string? Read(string key) => Values.GetValueOrDefault(key);
        public void Write(string key, string value, string? expected) { if (Read(key) != expected) throw new IOException("conflict"); Values[key] = value; }
    }

    [Fact]
    public void TutorialProgressIsAnOptionalProfileFieldThatSurvivesAvatarChanges()
    {
        var storage = new MemoryStorage();
        var store = new PlayerProfileStore(storage);
        Assert.Equal("", store.Tutorial.Prompt); Assert.Empty(store.Tutorial.Completed);
        store.AnswerTutorialPrompt(TutorialProgress.Skipped);
        store.CompleteLesson("basics"); store.CompleteLesson("basics"); store.CompleteLesson("taunt");
        store.SetAvatar("builtin:grove");
        var reloaded = new PlayerProfileStore(storage);
        Assert.Equal("builtin:grove", reloaded.Avatar);
        Assert.Equal(TutorialProgress.Skipped, reloaded.Tutorial.Prompt);
        Assert.Equal(["basics", "taunt"], reloaded.Tutorial.Completed);
        Assert.True(reloaded.TutorialDone("taunt"));
        // An avatar-only save from before the tutorial update still loads, and stays byte-identical when nothing tutorial-related was saved.
        var old = new MemoryStorage(); old.Values["profile"] = "{\"Avatar\":\"builtin:sage\"}";
        var legacy = new PlayerProfileStore(old);
        Assert.Equal("builtin:sage", legacy.Avatar); Assert.Equal("", legacy.Tutorial.Prompt);
        legacy.SetAvatar("builtin:sage"); Assert.Equal("{\"Avatar\":\"builtin:sage\"}", old.Values["profile"]);
        // Unknown or hostile lesson ids are dropped instead of breaking the profile.
        var parsed = PlayerProfileStore.Parse("{\"Avatar\":\"builtin:sage\",\"Tutorial\":{\"Prompt\":\"maybe\",\"Completed\":[\"basics\",\"<b>\",\"basics\"]}}");
        Assert.Equal("", parsed.Tutorial!.Prompt); Assert.Equal(["basics"], parsed.Tutorial.Completed);
        store.ResetTutorialProgress(); Assert.Empty(new PlayerProfileStore(storage).Tutorial.Completed);
        Assert.Equal(TutorialProgress.Skipped, new PlayerProfileStore(storage).Tutorial.Prompt);
    }

    [Fact]
    public void DamagedProfileIsNotOverwrittenByTutorialProgress()
    {
        var storage = new MemoryStorage(); storage.Values["profile"] = "{\"Avatar\":\"not-a-picture\"}";
        var store = new PlayerProfileStore(storage);
        store.CompleteLesson("basics");
        Assert.True(store.TutorialDone("basics"));
        Assert.Equal("{\"Avatar\":\"not-a-picture\"}", storage.Values["profile"]);
    }
}
