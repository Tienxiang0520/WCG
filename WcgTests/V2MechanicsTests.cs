using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;

/// <summary>
/// v2.0 改版專屬機制測試：格擋、越陣、魔免、重置能量、自選棄牌、當前 PP 門檻、轉直立卡面限制「本回合不能攻擊玩家」、幸運幣減費、省略陣營目標與 AI 合法性。
/// </summary>
public class V2MechanicsTests
{
    private readonly CardDatabase cards;

    public V2MechanicsTests()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(x => x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb")));
        cards = new(env.Object);
    }

    private GameEngine Game(bool playerFirst = true)
    {
        var g = new GameEngine(cards, new Random(23));
        g.StartGame(cards.PresetDecks[0], cards.PresetDecks[1], playerFirst);
        foreach (var p in new[] { g.Player, g.Computer })
        {
            p.Hand.Clear();
            p.Field.Clear();
            p.Structures.Clear();
            p.Graveyard.Clear();
            p.EnergyZone.Clear();
            for (int i = 0; i < 12; i++) p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));
        }
        return g;
    }

    private CardInstance Hand(GameEngine g, string id, PlayerState? p = null)
    {
        var c = new CardInstance(cards.GetCard(id)!);
        (p ?? g.Player).Hand.Add(c);
        return c;
    }

    private MonsterInstance Unit(PlayerState p, string id, int slot, bool tapped = false)
    {
        var m = new MonsterInstance(cards.GetCard(id)!) { Slot = slot, IsTapped = tapped, HasSummoningSickness = false };
        p.Field.Add(m);
        return m;
    }

    private static void Refresh(GameEngine g) =>
        typeof(GameEngine).GetMethod("RefreshBoard", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(g, null);

    private static void Choose(GameEngine g, Func<ChoiceOption, bool> pick) =>
        Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(pick)));

    private static void Settle(GameEngine g, int slot = 2)
    {
        for (int i = 0; i < 80 && g.IsWaiting && !g.IsOver; i++)
        {
            if (g.CurrentPendingChoice is { } c)
                Choose(g, o => o.Id == $"SLOT:{slot}" || o.Id != "SKIP");
            else
            {
                var t = g.CurrentPendingTarget!;
                if (t.PlayerValidator != null) Assert.True(g.SelectPlayerTarget(g.Player));
                else Assert.True(g.SelectTarget(g.Player.Board.Concat(g.Computer.Board).First(x => t.Validator!(x))));
            }
        }
        Assert.False(g.IsWaiting);
    }

    [Fact]
    public void BlockTapsUprightDefenderToSurviveLethalTieAndPoisonButTappedDefenderDies()
    {
        var g = Game();
        Unit(g.Computer, "WCG-063", 0); // → 右鄰獲得格擋
        var defender = Unit(g.Computer, "WCG-101", 1); // PP 10，直立且具格擋
        var bigAttacker = Unit(g.Player, "WCG-155", 0); // PP 16
        var tieAttacker = Unit(g.Player, "WCG-101", 1); // PP 10
        var poisonAttacker = Unit(g.Player, "WCG-382", 2); // 劇毒 PP 5
        Refresh(g);

        Assert.True(defender.HasShield);
        Assert.True(g.Attack(g.Player, bigAttacker, defender));
        Settle(g);
        Assert.Contains(defender, g.Computer.Field);
        Assert.True(defender.IsTapped);

        // 已橫置後無法再格擋，平手/致死/劇毒都會陣亡。
        defender.IsTapped = false;
        Assert.True(g.Attack(g.Player, tieAttacker, defender));
        Settle(g);
        Assert.Contains(defender, g.Computer.Field);
        Assert.True(defender.IsTapped);

        Assert.True(g.Attack(g.Player, poisonAttacker, defender));
        Settle(g);
        Assert.DoesNotContain(defender, g.Computer.Field);

        // 若面對劇毒時仍是直立格擋，一樣橫置存活（使用無嘲諷的聖印侍從在 1 格給 2 格格擋）。
        var freshDefender = Unit(g.Computer, "WCG-101", 2);
        var freshPoison = Unit(g.Player, "WCG-382", 3);
        Unit(g.Computer, "WCG-063", 1); // → 右鄰獲得格擋
        Refresh(g);
        Assert.True(freshDefender.HasShield);
        Assert.True(g.Attack(g.Player, freshPoison, freshDefender));
        Settle(g);
        Assert.Contains(freshDefender, g.Computer.Field);
        Assert.True(freshDefender.IsTapped);
    }

    [Fact]
    public void BlockWorksInDuelDoesNotStopSpellRemovalAndIsBlockedByArbiterUpArrow()
    {
        // 決鬥時直立的被攻擊者也會觸發格擋而橫置存活。
        var g = Game();
        Unit(g.Computer, "WCG-063", 0);
        var shielded = Unit(g.Computer, "WCG-101", 1);
        Refresh(g);
        Assert.True(shielded.HasShield);
        Assert.True(g.SummonMonster(g.Player, Hand(g, "WCG-266")));
        Choose(g, o => o.Id == "SLOT:0");
        Choose(g, o => o.Id == shielded.InstanceId.ToString());
        Assert.Contains(shielded, g.Computer.Field);
        Assert.True(shielded.IsTapped);

        // 格擋不防法術消滅或彈回。
        var intact = Unit(g.Computer, "WCG-101", 2);
        Unit(g.Computer, "WCG-069", 3);
        Refresh(g);
        Assert.True(intact.HasShield);
        Assert.True(g.CastSpellAt(g.Player, Hand(g, "WCG-004"), intact));
        Assert.DoesNotContain(intact, g.Computer.Field);

        // 聖堂仲裁護衛 ↑ 指向對面敵怪（4 - slot），使其失去且不能獲得格擋。
        var g2 = Game();
        Unit(g2.Computer, "WCG-063", 0);
        var blocked = Unit(g2.Computer, "WCG-101", 1); // 電腦 slot 1 正對玩家 slot 3
        Unit(g2.Player, "WCG-147", 3);
        var killer = Unit(g2.Player, "WCG-155", 0);
        Refresh(g2);
        Assert.False(blocked.HasShield);
        Assert.True(g2.Attack(g2.Player, killer, blocked));
        Assert.DoesNotContain(blocked, g2.Computer.Field);
    }

    [Fact]
    public void OathCounterAttachGrantsBlockToChosenMonsterOnEitherSide()
    {
        var g = Game();
        var ogre = Unit(g.Computer, "WCG-156", 0);
        var target = Unit(g.Player, "WCG-101", 0);
        Hand(g, "WCG-198");
        Assert.True(g.EndTurn());
        Settle(g);

        Assert.True(g.Attack(g.Computer, ogre));
        Choose(g, o => o.Id == g.Player.Hand.Single(c => c.Card.Id == "WCG-198").InstanceId.ToString());
        Assert.Equal(7, g.Player.Hp); // 6 -> 抵銷並回復 2 上限 7
        Choose(g, o => o.Id == target.InstanceId.ToString());
        Assert.True(target.HasShield);
    }

    [Fact]
    public void BypassAndGaiaTyrantTauntThresholdsFollowV2Rules()
    {
        var g = Game();
        var smallTaunt = Unit(g.Computer, "WCG-129", 0); // PP 9 嘲諷
        var scout = Unit(g.Player, "WCG-261", 0); // 越陣
        var tyrant = Unit(g.Player, "WCG-059", 1); // 蓋亞暴龍（PP 18 以上嘲諷仍可阻擋）
        Refresh(g);

        // 對面只有 PP 9 嘲諷時，兩者都能越陣直接打玩家。
        Assert.Contains(smallTaunt, g.Computer.Field);
        Assert.True(g.CanAttackPlayer(scout));
        Assert.True(g.CanAttackPlayer(tyrant));

        // 加入 PP 18 的森金持盾衛（WCG-113）後，WCG-261 仍可直接打玩家，蓋亞暴龍則被擋住。
        var heavyTaunt = Unit(g.Computer, "WCG-113", 1);
        Refresh(g);
        Assert.True(g.CanAttackPlayer(scout));
        Assert.False(g.CanAttackPlayer(tyrant));
        Assert.Equal([heavyTaunt], g.GetAttackTargets(tyrant));
    }

    [Fact]
    public void SpellwardPreventsTargetedSpellsFromEitherPlayer()
    {
        var g = Game();
        var ownGolem = Unit(g.Player, "WCG-152", 0);
        var enemyHermit = Unit(g.Computer, "WCG-229", 0);
        var plainAlly = Unit(g.Player, "WCG-101", 1);
        Refresh(g);

        var bounce = Hand(g, "WCG-026");
        var buff = Hand(g, "WCG-135");
        var silence = Hand(g, "WCG-072");

        Assert.DoesNotContain(ownGolem, g.GetDirectPlayTargets(g.Player, bounce));
        Assert.DoesNotContain(enemyHermit, g.GetDirectPlayTargets(g.Player, bounce));
        Assert.DoesNotContain(ownGolem, g.GetDirectPlayTargets(g.Player, buff));
        Assert.DoesNotContain(enemyHermit, g.GetDirectPlayTargets(g.Player, buff));
        Assert.DoesNotContain(ownGolem, g.GetDirectPlayTargets(g.Player, silence));
        Assert.DoesNotContain(enemyHermit, g.GetDirectPlayTargets(g.Player, silence));
        Assert.Contains(plainAlly, g.GetDirectPlayTargets(g.Player, buff));
    }

    [Fact]
    public void UntapEffectsWcg374AndWcg126RestoreTappedEnergy()
    {
        var g = Game();
        // 先橫置 3 張能量，留下 9 張直立能量支付費用。
        for (int i = 0; i < 3; i++) g.Player.EnergyZone[i].IsTapped = true;
        var beforeTapped = g.Player.EnergyZone.Count(e => e.IsTapped);

        // 遠古母樹根鬚（WCG-126）：橫置重置 2 點能量。
        Assert.True(g.PlayEnchantment(g.Player, Hand(g, "WCG-126")));
        Choose(g, o => o.Id == "SLOT:0"); // 花 3 能量 -> 6 橫置
        var root = g.Player.Structures.Single();
        Assert.True(g.Activate(g.Player, root));
        Assert.Equal(beforeTapped + 3 - 2, g.Player.EnergyZone.Count(e => e.IsTapped));

        // 回潮術士（WCG-374）：彈回另 1 隻己怪並重置 1 點能量。
        var ally = Unit(g.Player, "WCG-101", 1);
        var tappedBefore374 = g.Player.EnergyZone.Count(e => e.IsTapped);
        Assert.True(g.SummonMonster(g.Player, Hand(g, "WCG-374")));
        Choose(g, o => o.Id == "SLOT:2"); // 花 2 能量
        Choose(g, o => o.Id == ally.InstanceId.ToString());
        Assert.Equal(tappedBefore374 + 2 - 1, g.Player.EnergyZone.Count(e => e.IsTapped));
        Assert.Contains(ally.Card, g.Player.Hand.Select(h => h.Card));
    }

    [Fact]
    public void ChosenDiscardCostsLetPlayerPickSpecificCards()
    {
        var g = Game();
        var target = Unit(g.Computer, "WCG-101", 0);
        var keep = Hand(g, "WCG-155");
        var fodder1 = Hand(g, "WCG-101");
        var fodder2 = Hand(g, "WCG-101");

        // WCG-084 靈魂撕裂：自選棄 1 張指定牌，保留不想棄的牌。
        var rip = Hand(g, "WCG-084");
        Assert.True(g.CastSpellAt(g.Player, rip, target));
        Assert.Equal(GameEngine.DiscardCostTitle, g.CurrentPendingChoice!.Title);
        Choose(g, o => o.Id == fodder1.InstanceId.ToString());
        Assert.Contains(keep, g.Player.Hand);
        Assert.Contains(fodder1, g.Player.Graveyard);
        Assert.DoesNotContain(target, g.Computer.Field);

        // WCG-095 縛魂巨怪：自選棄 2 張牌（保留 keep 需再補 1 張）。
        var fodder3 = Hand(g, "WCG-101");
        var fiend = Hand(g, "WCG-095");
        Assert.True(g.SummonMonster(g.Player, fiend));
        Assert.StartsWith(GameEngine.DiscardCostTitle, g.CurrentPendingChoice!.Title);
        Choose(g, o => o.Id == fodder2.InstanceId.ToString());
        Assert.StartsWith(GameEngine.DiscardCostTitle, g.CurrentPendingChoice!.Title);
        Choose(g, o => o.Id == fodder3.InstanceId.ToString());
        Choose(g, o => o.Id == "SLOT:0");
        Assert.Contains(keep, g.Player.Hand);
        Assert.Contains(fodder2, g.Player.Graveyard);
        Assert.Contains(fodder3, g.Player.Graveyard);
        Assert.Single(g.Player.Field, m => m.Card.Id == "WCG-095");
    }

    [Fact]
    public void Wcg385ChecksCurrentPpNotPrintedPpForBonus()
    {
        var g = Game();
        var hunter = Unit(g.Player, "WCG-385", 0); // PP 13；攻擊 PP 10 以下怪物時本回合 PP +5
        var buffedLow = Unit(g.Computer, "WCG-101", 0); // 印刷 PP 10，加 +5 變 15
        Assert.True(g.CastSpellAt(g.Player, Hand(g, "WCG-135"), buffedLow));
        Assert.Equal(15, buffedLow.CurrentPP);

        // 面對目前 PP 15 的 WCG-101，不觸發 +5；因雙方都沒格擋、獵手具劇毒且 13 < 15 兩敗俱傷。
        Assert.True(g.Attack(g.Player, hunter, buffedLow));
        Assert.DoesNotContain(hunter, g.Player.Field);

        // 相反地，淡水狂鱷（印刷 13）被孢沼爬蟲降到目前 PP 10 時，會觸發 +5 變成 18 PP 並擊敗對手存活！
        var g2 = Game();
        var h2 = Unit(g2.Player, "WCG-385", 0);
        var shrunk = Unit(g2.Computer, "WCG-154", 0); // 13 PP
        shrunk.Attachments.Add(new(new(cards.GetCard("WCG-382")!), g2.Player.Id));
        Refresh(g2);
        Assert.Equal(10, shrunk.CurrentPP);
        Assert.True(g2.Attack(g2.Player, h2, shrunk));
        Assert.Equal(18, h2.CurrentPP);
        Assert.Contains(h2, g2.Player.Field);
        Assert.DoesNotContain(shrunk, g2.Computer.Field);
    }

    [Fact]
    public void UntapEffectsSetCannotAttackPlayerWithoutHiddenGlobalCap()
    {
        // 普通攻擊玩家不會再上隱藏上限；只有寫明「本回合不能攻擊玩家」的轉直立牌才會禁止打玩家，但仍可攻擊敵方怪物與觸發貫穿。
        var g = Game();
        var trampler = Unit(g.Player, "WCG-172", 0); // 貫穿，PP 18 / DP 2
        var foe = Unit(g.Computer, "WCG-101", 0);
        var startHp = g.Computer.Hp;

        Assert.True(g.Attack(g.Player, trampler));
        Assert.Equal(startHp - 2, g.Computer.Hp);
        Assert.False(trampler.SurgeNoFace);

        // 再起戰吼（WCG-392）轉直立後不能再直接打玩家，但攻擊怪物仍能造成貫穿傷害。
        Assert.True(g.CastSpellAt(g.Player, Hand(g, "WCG-392"), trampler));
        Assert.False(trampler.IsTapped);
        Assert.True(trampler.SurgeNoFace);
        Assert.False(g.CanAttackPlayer(trampler));
        Assert.False(g.Attack(g.Player, trampler));
        Assert.True(g.Attack(g.Player, trampler, foe));
        Assert.DoesNotContain(foe, g.Computer.Field);
        Assert.Equal(startHp - 3, g.Computer.Hp);

        // 其餘 3 張轉直立效果（WCG-143 / WCG-167 / WCG-145）同樣寫明並設定本回合不能攻擊玩家。
        foreach (var id in new[] { "WCG-143", "WCG-167" })
        {
            var u = Unit(g.Player, "WCG-156", 1);
            u.IsTapped = true;
            Assert.True(g.CastSpellAt(g.Player, Hand(g, id), u));
            Assert.False(u.IsTapped);
            Assert.True(u.SurgeNoFace);
            Assert.False(g.CanAttackPlayer(u));
            g.Player.Field.Remove(u);
        }

        var ally = Unit(g.Player, "WCG-156", 1);
        ally.IsTapped = true;
        Assert.True(g.SummonMonster(g.Player, Hand(g, "WCG-145")));
        Choose(g, o => o.Id == "SLOT:2");
        Choose(g, o => o.Id == "READY");
        Assert.True(g.SelectTarget(ally));
        Assert.False(ally.IsTapped);
        Assert.True(ally.SurgeNoFace);
        Assert.False(g.CanAttackPlayer(ally));
    }

    [Fact]
    public void LuckyCoinWcg263AndOmittedSideTargetingWorkAsSpecified()
    {
        var g = new GameEngine(cards, new Random(23));
        g.StartGame(cards.PresetDecks[0], cards.PresetDecks[1], playerFirst: false);
        Assert.Equal(6, g.Computer.Hp);
        Assert.Equal(7, g.Player.Hp);
        var coin = g.Player.Hand.Single(c => c.Card.Id == GameEngine.LuckyCoinId);
        Assert.Equal("本回合你打出的下 1 張牌費用 -1。", coin.Card.Text);

        // 先給玩家行動權並鋪場測試幸運幣與無陣營限定法術。
        Assert.True(g.EndTurn());
        while (g.CurrentPendingChoice is { } c) Assert.True(g.SelectChoice(c.Options.Last()));
        Assert.Equal("player", g.CurrentTurnPlayerId);

        g.Player.EnergyZone.Clear();
        g.Player.EnergyZone.Add(new(cards.GetCard("WCG-101")!)); // 僅 1 點能量
        var m = Unit(g.Player, "WCG-101", 0);
        var foe = Unit(g.Computer, "WCG-101", 0);
        var bounce = Hand(g, "WCG-026"); // 原價 2 費

        Assert.Equal(2, g.ActualCost(g.Player, bounce.Card));
        Assert.False(g.CanPlayCard(g.Player, bounce));
        Assert.True(g.CastSpell(g.Player, coin));
        Assert.Contains(coin, g.Player.Graveyard);
        Assert.Equal(1, g.ActualCost(g.Player, bounce.Card));
        Assert.True(g.CastSpellAt(g.Player, bounce, m)); // 省略「敵方」-> 也可指自己場上的怪物
        Assert.Contains(bounce.Card, g.Player.Graveyard.Select(x => x.Card));

        // 1 位玩家目標可選己方或敵方。
        var heal = Hand(g, "WCG-070");
        g.Player.EnergyZone.Add(new(cards.GetCard("WCG-101")!));
        var pTargets = g.GetDirectPlayerTargets(g.Player, heal);
        Assert.Contains(g.Player, pTargets);
        Assert.Contains(g.Computer, pTargets);
    }

    [Fact]
    public void BuffedUtilityAndCombatCardsWorkAsSpecified()
    {
        // WCG-036: bounce 1 ally monster, untap 1 energy, draw 1 card
        var g = Game(playerFirst: false);
        Assert.True(g.EndTurn());
        while (g.CurrentPendingChoice is { } ch) Assert.True(g.SelectChoice(ch.Options.Last()));
        Assert.Equal("player", g.CurrentTurnPlayerId);
        g.Player.Hand.Clear();

        var ally = Unit(g.Player, "WCG-101", 0);
        g.Player.EnergyZone[0].IsTapped = true; // 1 tapped beforehand, 11 available
        var availBefore = g.Player.AvailableEnergy;
        var recall = Hand(g, "WCG-036");
        Assert.True(g.CastSpellAt(g.Player, recall, ally));
        Assert.DoesNotContain(ally, g.Player.Field);
        Assert.Contains(ally.Card, g.Player.Hand.Select(x => x.Card));
        Assert.Equal(availBefore, g.Player.AvailableEnergy); // paid 1, untapped 1 -> unchanged
        Assert.Equal(2, g.Player.Hand.Count); // bounced monster + 1 drawn

        // WCG-128: tap & sacrifice 1 ally monster -> draw 1 and opponent loses 1 life
        var g2 = Game(playerFirst: false);
        Assert.True(g2.EndTurn());
        while (g2.CurrentPendingChoice is { } ch2) Assert.True(g2.SelectChoice(ch2.Options.Last()));
        g2.Player.Hand.Clear();
        var well = new MonsterInstance(cards.GetCard("WCG-128")!) { Slot = 0, IsTapped = false };
        g2.Player.Structures.Add(well);
        var sac = Unit(g2.Player, "WCG-101", 1);
        var compHpBefore = g2.Computer.Hp;
        Assert.True(g2.Activate(g2.Player, well));
        Assert.NotNull(g2.CurrentPendingTarget);
        Assert.True(g2.SelectTarget(sac));
        Assert.True(well.IsTapped);
        Assert.DoesNotContain(sac, g2.Player.Field);
        Assert.Single(g2.Player.Hand);
        Assert.Equal(compHpBefore - 1, g2.Computer.Hp);

        // WCG-149: tap, bottom 1 card from hand, draw 1 and recover 1 life
        var bazaar = new MonsterInstance(cards.GetCard("WCG-149")!) { Slot = 2, IsTapped = false };
        g2.Player.Structures.Add(bazaar);
        g2.Player.Hp = 5;
        var bottomCard = Hand(g2, "WCG-101");
        Assert.True(g2.Activate(g2.Player, bazaar));
        if (g2.CurrentPendingChoice is { } bazaarChoice)
            Assert.True(g2.SelectChoice(bazaarChoice.Options.First(o => o.Id == bottomCard.InstanceId.ToString())));
        Assert.True(bazaar.IsTapped);
        Assert.Equal(bottomCard, g2.Player.Deck[^1]);
        Assert.Equal(2, g2.Player.Hand.Count);
        Assert.Equal(6, g2.Player.Hp);

        // WCG-165: lose 1 life, +6 PP and Trample on next combat, then to graveyard
        var g3 = Game(playerFirst: false);
        Assert.True(g3.EndTurn());
        while (g3.CurrentPendingChoice is { } ch3) Assert.True(g3.SelectChoice(ch3.Options.Last()));
        var attacker = Unit(g3.Player, "WCG-101", 0);
        var enemyTapped = Unit(g3.Computer, "WCG-101", 0, tapped: true);
        var frenzy = Hand(g3, "WCG-165");
        var pHpBefore = g3.Player.Hp;
        var cHpBefore = g3.Computer.Hp;
        Assert.True(g3.CastSpellAt(g3.Player, frenzy, attacker));
        Assert.Equal(pHpBefore - 1, g3.Player.Hp);
        Assert.Equal(16, attacker.CurrentPP);
        Assert.True(attacker.HasTrample);
        Assert.True(g3.Attack(g3.Player, attacker, enemyTapped));
        Assert.DoesNotContain(enemyTapped, g3.Computer.Field);
        Assert.Equal(cHpBefore - 1, g3.Computer.Hp);
        Assert.Contains(frenzy, g3.Player.Graveyard);
        Assert.Equal(10, attacker.CurrentPP);
        Assert.False(attacker.HasTrample);

        // WCG-294: untap all tapped allies, +3 PP this turn, can attack monsters but not players
        var g4 = Game(playerFirst: false);
        Assert.True(g4.EndTurn());
        while (g4.CurrentPendingChoice is { } ch4) Assert.True(g4.SelectChoice(ch4.Options.Last()));
        var tappedAlly = Unit(g4.Player, "WCG-101", 0, tapped: true);
        var tappedEnemy = Unit(g4.Computer, "WCG-101", 0, tapped: true);
        var horn = Hand(g4, "WCG-294");
        Assert.True(g4.CastSpell(g4.Player, horn));
        Assert.False(tappedAlly.IsTapped);
        Assert.Equal(13, tappedAlly.CurrentPP);
        Assert.True(g4.CanAttack(tappedAlly));
        Assert.False(g4.CanAttackPlayer(tappedAlly));
        Assert.Contains(tappedEnemy, g4.GetAttackTargets(tappedAlly));
        Assert.True(g4.Attack(g4.Player, tappedAlly, tappedEnemy));
        Assert.DoesNotContain(tappedEnemy, g4.Computer.Field);

        // WCG-297: +3 PP (upright or tapped) and grants Block
        var guard = Unit(g4.Player, "WCG-101", 1, tapped: true);
        var prayer = Hand(g4, "WCG-297");
        Assert.True(g4.CastSpellAt(g4.Player, prayer, guard));
        Assert.Equal(13, guard.CurrentPP);
        Assert.True(guard.HasShield);
        guard.IsTapped = false;
        Assert.Equal(13, guard.CurrentPP);
        Assert.True(guard.HasShield);
    }

    [Fact]
    public void ZeroPpDestroysImmediatelyTriggersDeathAndBypassesBlock()
    {
        var g = Game(playerFirst: false);
        Assert.True(g.EndTurn());
        while (g.CurrentPendingChoice is { } ch) Assert.True(g.SelectChoice(ch.Options.Last()));

        var graver = new CardInstance(cards.GetCard("WCG-101")!);
        var g2 = Game(playerFirst: false);
        var giver2 = Unit(g2.Computer, "WCG-063", 0);
        var sprout2 = Unit(g2.Computer, "WCG-043", 1);
        g2.Computer.Graveyard.Add(graver);
        Assert.True(g2.EndTurn());
        while (g2.CurrentPendingChoice is { } ch) Assert.True(g2.SelectChoice(ch.Options.Last()));
        Assert.True(sprout2.HasShield);
        Assert.False(sprout2.IsTapped);

        var handBefore = g2.Player.Hand.Count;
        Assert.True(g2.CastSpellAt(g2.Player, Hand(g2, "WCG-004"), sprout2)); // -10 PP + draw 1
        if (g2.CurrentPendingChoice is { } recoverChoice)
            Assert.True(g2.SelectChoice(recoverChoice.Options.First()));

        Assert.DoesNotContain(sprout2, g2.Computer.Field);
        Assert.Contains(g2.Computer.Graveyard, c => c.InstanceId == sprout2.InstanceId);
        Assert.Contains(graver, g2.Computer.Hand); // On Death fired via PP 0 destruction (bypassing upright Block)
        Assert.Equal(handBefore + 1, g2.Player.Hand.Count); // Hand(WCG-004) added after snapshot: -1 cast +1 draw => +1 total

        // Own death listener WCG-011 also fires when a friendly unit dies to PP 0
        var g3 = Game(playerFirst: false);
        var b3 = Unit(g3.Player, "WCG-011", 0);
        var boosted = Unit(g3.Player, "WCG-101", 1); // 10 PP
        var buffer = Unit(g3.Player, "WCG-362", 2); // 4 PP, <- -> allies +3 PP => boosted = 13 PP
        Assert.True(g3.EndTurn());
        while (g3.CurrentPendingChoice is { } ch2) Assert.True(g3.SelectChoice(ch2.Options.Last()));
        Assert.Equal(13, boosted.CurrentPP);
        Assert.True(g3.CastSpellAt(g3.Player, Hand(g3, "WCG-082"), boosted)); // 13 - 10 = 3 PP (survives while buffer is on board)
        Assert.Contains(boosted, g3.Player.Field);
        Assert.Equal(3, boosted.CurrentPP);
        var h2 = g3.Player.Hand.Count;
        // Now remove buffer via WCG-082 (-10 on 4 PP) -> buffer dies (draws 1), then boosted drops 3 -> 0 PP and dies (draws 1 more)
        Assert.True(g3.CastSpellAt(g3.Player, Hand(g3, "WCG-082"), buffer));
        Assert.DoesNotContain(buffer, g3.Player.Field);
        Assert.DoesNotContain(boosted, g3.Player.Field);
        Assert.Contains(b3, g3.Player.Field);
        Assert.Equal(h2 + 2, g3.Player.Hand.Count);
    }

    [Fact]
    public void CombatOnlyDebuffAttachmentsApplyOnlyInCombatAndRespectBlock()
    {
        var g = Game(playerFirst: false);
        var small5 = Unit(g.Computer, "WCG-319", 1); // 5 PP
        var small6 = Unit(g.Computer, "WCG-132", 2); // 6 PP
        var blocker = Unit(g.Computer, "WCG-063", 0); // -> Block onto slot 1
        Assert.True(g.EndTurn());
        while (g.CurrentPendingChoice is { } ch) Assert.True(g.SelectChoice(ch.Options.Last()));
        Assert.True(small5.HasShield);

        var sup5 = Hand(g, "WCG-168"); // Next combat -5 PP
        var tend7 = Hand(g, "WCG-284"); // Next combat -7 PP
        Assert.True(g.CastSpellAt(g.Player, sup5, small5));
        Assert.True(g.CastSpellAt(g.Player, tend7, small6));

        // Out of combat: neither dies from 0 PP and PP reads unchanged
        Assert.Contains(small5, g.Computer.Field);
        Assert.Equal(5, small5.CurrentPP);
        Assert.Contains(small6, g.Computer.Field);
        Assert.Equal(6, small6.CurrentPP);

        // Combat against upright Blocked 5-PP monster with WCG-168 (-5): combat PP is 0 < 5, but Block saves it!
        var atk5 = Unit(g.Player, "WCG-319", 3);
        Assert.True(g.Attack(g.Player, atk5, small5));
        Assert.Contains(small5, g.Computer.Field);
        Assert.True(small5.IsTapped);
        Assert.Equal(5, small5.CurrentPP);
        Assert.Contains(sup5, g.Player.Graveyard);

        // Combat against 19-PP upright monster with Block + WCG-284 (-7 -> 12 PP vs 13 PP attacker) also saves via Block
        var tall = Unit(g.Computer, "WCG-114", 4); // 19 PP
        var shieldGrant = Hand(g, "WCG-297");
        Assert.True(g.CastSpellAt(g.Player, shieldGrant, tall)); // +3 PP => 22 PP + Block
        var tend2 = Hand(g, "WCG-284");
        Assert.True(g.CastSpellAt(g.Player, tend2, tall));
        Assert.Equal(22, tall.CurrentPP);
        var atk16 = Unit(g.Player, "WCG-155", 4); // 16 PP vs (22 - 7 = 15 PP)
        Assert.True(g.Attack(g.Player, atk16, tall));
        Assert.Contains(tall, g.Computer.Field);
        Assert.True(tall.IsTapped);
        Assert.Equal(22, tall.CurrentPP);
        Assert.Contains(tend2, g.Player.Graveyard);
    }

    [Fact]
    public void DebuffReworkSingleTargetAoeAndKeepersWorkAsSpecified()
    {
        var g = Game(playerFirst: false);
        Assert.True(g.EndTurn());
        while (g.CurrentPendingChoice is { } ch) Assert.True(g.SelectChoice(ch.Options.Last()));

        var giant = Unit(g.Computer, "WCG-119", 4); // 26 PP (in lane 4 -> opposite ally slot 0)
        var footman = Unit(g.Computer, "WCG-101", 2); // 10 PP

        // WCG-082 (-10 PP this turn) drops 26 -> 16 PP, then recovers to 26 on EndTurn
        Assert.True(g.CastSpellAt(g.Player, Hand(g, "WCG-082"), giant));
        Assert.Equal(16, giant.CurrentPP);
        Assert.Contains(giant, g.Computer.Field);

        // WCG-032 on upright giant gives -10 (16 -> 6 PP); on tapped giant destroys outright
        Assert.True(g.CastSpellAt(g.Player, Hand(g, "WCG-032"), giant));
        Assert.Equal(6, giant.CurrentPP);

        Assert.True(g.EndTurn());
        while (g.CurrentPendingChoice is { } ch2) Assert.True(g.SelectChoice(ch2.Options.Last()));
        Assert.Equal(26, giant.CurrentPP);
        Assert.True(g.EndTurn());
        while (g.CurrentPendingChoice is { } ch3) Assert.True(g.SelectChoice(ch3.Options.Last()));

        giant.IsTapped = true;
        Assert.True(g.CastSpellAt(g.Player, Hand(g, "WCG-032"), giant));
        Assert.DoesNotContain(giant, g.Computer.Field);

        // WCG-326 Trailblazer Vanguard gives opposite monster -12 PP this turn
        var giant2 = Unit(g.Computer, "WCG-119", 4); // lane 4 == opposite ally slot 0
        var van = Hand(g, "WCG-326");
        Assert.True(g.SummonMonster(g.Player, van));
        Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(o => o.Id == "SLOT:0")));
        if (g.CurrentPendingTarget != null) Assert.True(g.SelectTarget(giant2));
        Assert.Equal(14, giant2.CurrentPP);

        // WCG-008 AOE: -10 PP to all monsters on both sides
        var ally10 = Unit(g.Player, "WCG-101", 1);
        Assert.True(g.CastSpell(g.Player, Hand(g, "WCG-008")));
        Assert.DoesNotContain(footman, g.Computer.Field);
        Assert.DoesNotContain(ally10, g.Player.Field);
        Assert.Equal(4, giant2.CurrentPP); // 14 - 10 = 4 PP

        // WCG-386 passive ↑ -3 PP + WCG-390 (-6 PP)
        var g390 = Game(playerFirst: false);
        Assert.True(g390.EndTurn());
        while (g390.CurrentPendingChoice is { } ch4) Assert.True(g390.SelectChoice(ch4.Options.Last()));
        var bog = new MonsterInstance(cards.GetCard("WCG-386")!) { Slot = 2, IsTapped = false };
        g390.Player.Structures.Add(bog);
        var target9 = Unit(g390.Computer, "WCG-101", 2); // 10 - 3 = 7 PP from ↑ -3
        Assert.True(g390.SummonMonster(g390.Player, Hand(g390, "WCG-390")));
        Assert.True(g390.SelectChoice(g390.CurrentPendingChoice!.Options.First(o => o.Id == "SLOT:3"))); // 7 - 6 = 1 PP left
        Assert.Equal(1, target9.CurrentPP);
    }

    [Fact]
    public void V2CatalogPresetsRunToCompletionWithAiWithoutStalling()
    {
        var g = new GameEngine(cards, new Random(41));
        g.StartGame(cards.PresetDecks[0], cards.PresetDecks[2]);
        g.Player.IsAi = true;
        g.Computer.IsAi = true;
        for (int i = 0; i < 2000 && !g.IsOver; i++)
            Assert.True(g.ExecuteAiStep());
        Assert.True(g.IsOver);
    }
}
