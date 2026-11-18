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

        // 聖堂仲裁護衛 ↑ 指向對面敵怪（4 - slot），使其 PP -3（不再禁止格擋）。
        var g2 = Game();
        Unit(g2.Computer, "WCG-063", 0);
        var blocked = Unit(g2.Computer, "WCG-101", 1); // 電腦 slot 1 正對玩家 slot 3
        Unit(g2.Player, "WCG-147", 3);
        var killer = Unit(g2.Player, "WCG-155", 0);
        Refresh(g2);
        Assert.True(blocked.HasShield);
        Assert.Equal(7, blocked.CurrentPP);
        Assert.True(g2.Attack(g2.Player, killer, blocked));
        Assert.Contains(blocked, g2.Computer.Field);
        Assert.True(blocked.IsTapped);
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

        // 加入 PP 18 的森金持盾衛（WCG-113）後，v2.2 蓋亞暴龍也為純越陣，兩者皆可直接打玩家。
        var heavyTaunt = Unit(g.Computer, "WCG-113", 1);
        Refresh(g);
        Assert.True(g.CanAttackPlayer(scout));
        Assert.True(g.CanAttackPlayer(tyrant));
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
        var hush = Hand(g, "WCG-208");

        Assert.DoesNotContain(ownGolem, g.GetDirectPlayTargets(g.Player, bounce));
        Assert.DoesNotContain(enemyHermit, g.GetDirectPlayTargets(g.Player, bounce));
        Assert.DoesNotContain(ownGolem, g.GetDirectPlayTargets(g.Player, buff));
        Assert.DoesNotContain(enemyHermit, g.GetDirectPlayTargets(g.Player, buff));
        Assert.DoesNotContain(ownGolem, g.GetDirectPlayTargets(g.Player, hush));
        Assert.DoesNotContain(enemyHermit, g.GetDirectPlayTargets(g.Player, hush));
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
        Assert.Equal(7, g.Computer.Hp);
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

        // WCG-149: tap, draw 1, discard 1, and recover 1 life
        var bazaar = new MonsterInstance(cards.GetCard("WCG-149")!) { Slot = 2, IsTapped = false };
        g2.Player.Structures.Add(bazaar);
        g2.Player.Hp = 5;
        var discardCard = Hand(g2, "WCG-101");
        Assert.True(g2.Activate(g2.Player, bazaar));
        if (g2.CurrentPendingChoice is { } bazaarChoice)
            Assert.True(g2.SelectChoice(bazaarChoice.Options.First(o => o.Id == discardCard.InstanceId.ToString())));
        Assert.True(bazaar.IsTapped);
        Assert.Contains(discardCard, g2.Player.Graveyard);
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
        var buffer = Unit(g3.Player, "WCG-362", 2); // 4 PP, <- -> allies +2 PP => boosted = 12 PP
        Assert.True(g3.EndTurn());
        while (g3.CurrentPendingChoice is { } ch2) Assert.True(g3.SelectChoice(ch2.Options.Last()));
        Assert.Equal(12, boosted.CurrentPP);
        Assert.True(g3.CastSpellAt(g3.Player, Hand(g3, "WCG-082"), boosted)); // 12 - 10 = 2 PP (survives while buffer is on board)
        Assert.Contains(boosted, g3.Player.Field);
        Assert.Equal(2, boosted.CurrentPP);
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

        var sup5 = Hand(g, "WCG-168"); // -5 PP immediately
        var tend7 = Hand(g, "WCG-284"); // -7 PP immediately
        Assert.True(g.CastSpellAt(g.Player, sup5, small5));
        Assert.True(g.CastSpellAt(g.Player, tend7, small6));

        // Immediate PP <= 0 kills both even when small5 has Block
        Assert.DoesNotContain(small5, g.Computer.Field);
        Assert.Contains(sup5, g.Player.Graveyard);
        Assert.DoesNotContain(small6, g.Computer.Field);
        Assert.Contains(tend7, g.Player.Graveyard);

        // Combat against 19-PP upright monster with Block + WCG-284 (-7 -> 15 PP vs 16 PP attacker) still saves via Block
        var tall = Unit(g.Computer, "WCG-114", 4); // 19 PP
        var shieldGrant = Hand(g, "WCG-297");
        Assert.True(g.CastSpellAt(g.Player, shieldGrant, tall)); // +3 PP => 22 PP + Block
        var tend2 = Hand(g, "WCG-284");
        Assert.True(g.CastSpellAt(g.Player, tend2, tall));
        Assert.Equal(15, tall.CurrentPP);
        var atk16 = Unit(g.Player, "WCG-155", 4); // 16 PP vs 15 PP
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
    public void V21RulesSyncBehaviorTests()
    {
        // 1. 先手 7 血、第 1 回合可出牌但不抽牌
        var g1 = new GameEngine(cards, new Random(7));
        g1.StartGame(cards.PresetDecks[0], cards.PresetDecks[1], playerFirst: true);
        Assert.Equal(7, g1.Player.Hp);
        Assert.Equal(7, g1.Computer.Hp);
        Assert.Equal(7, g1.Player.Hand.Count);
        Assert.Equal(8, g1.Computer.Hand.Count);
        var res = g1.Player.Hand[0];
        Assert.True(g1.PlayEnergy(g1.Player, res));
        var oneDrop = g1.Player.Hand.FirstOrDefault(c => c.Card.TotalCost <= 1);
        if (oneDrop != null) Assert.True(g1.CanPlayCard(g1.Player, oneDrop));

        // 2. 攻擊者宣告即橫置所以不能格擋（即使被反擊抵銷也保持橫置），且 WCG-136 攻擊後觸發正常
        var g2 = Game();
        var atk = Unit(g2.Player, "WCG-101", 0); // 10 PP
        var shieldGrant = Hand(g2, "WCG-297"); // +3 PP + 格擋 => 13 PP
        Assert.True(g2.CastSpellAt(g2.Player, shieldGrant, atk));
        var big = Unit(g2.Computer, "WCG-114", 0); // 19 PP
        Assert.True(g2.Attack(g2.Player, atk, big));
        Assert.DoesNotContain(atk, g2.Player.Field); // 攻擊者宣告即橫置，不能格擋

        // 被反擊抵銷的攻擊仍維持橫置
        var g2b = Game();
        var faceAtk = Unit(g2b.Player, "WCG-101", 0);
        var barrier = new CardInstance(cards.GetCard("WCG-196")!);
        g2b.Computer.Hand.Add(barrier);
        for (int i = 0; i < 4; i++) g2b.Computer.EnergyZone.Add(new(cards.GetCard("WCG-101")!));
        Assert.True(g2b.Attack(g2b.Player, faceAtk));
        Choose(g2b, o => o.Id == barrier.InstanceId.ToString());
        Assert.True(faceAtk.IsTapped);

        // WCG-136 攻擊後仍可觸發減 PP
        var g136 = Game();
        var scout = Unit(g136.Player, "WCG-136", 0);
        var foe = Unit(g136.Computer, "WCG-114", 0);
        Assert.True(g136.Attack(g136.Player, scout));
        Assert.True(g136.IsWaiting);
        Choose(g136, o => o.Id == foe.InstanceId.ToString());
        Assert.Equal(9, foe.CurrentPP);

        // 3. 決鬥雙方直立且有格擋都可格擋存活
        var g3c = Game();
        var nature = Unit(g3c.Player, "WCG-041", 0); // 13 PP 生機
        var foeBlk = Unit(g3c.Computer, "WCG-041", 0); // 13 PP
        Assert.True(g3c.CastSpellAt(g3c.Player, Hand(g3c, "WCG-297"), nature)); // 16 PP + 格擋
        Assert.True(g3c.CastSpellAt(g3c.Player, Hand(g3c, "WCG-297"), foeBlk)); // 16 PP + 格擋
        Assert.True(g3c.CastSpellAt(g3c.Player, Hand(g3c, "WCG-248"), nature));
        Assert.True(g3c.SelectTarget(foeBlk));
        Assert.Contains(nature, g3c.Player.Field);
        Assert.Contains(foeBlk, g3c.Computer.Field);
        Assert.True(nature.IsTapped);
        Assert.True(foeBlk.IsTapped);

        // 4. 143 / 165 / 271 立即加 PP；168 / 284 立即歸零消滅
        var g4 = Game();
        var u1 = Unit(g4.Player, "WCG-101", 0); // 10 PP
        u1.IsTapped = true;
        Assert.True(g4.CastSpellAt(g4.Player, Hand(g4, "WCG-143"), u1));
        Assert.Equal(15, u1.CurrentPP);
        Assert.False(u1.IsTapped);
        Assert.True(u1.SurgeNoFace);
        Assert.False(g4.CanAttackPlayer(u1));
        var u2 = Unit(g4.Player, "WCG-101", 1); // 10 PP
        Assert.True(g4.CastSpellAt(g4.Player, Hand(g4, "WCG-165"), u2));
        Assert.Equal(16, u2.CurrentPP);
        Assert.True(u2.HasTrample);
        var u3 = Unit(g4.Player, "WCG-101", 2); // 10 PP
        Assert.True(g4.CastSpellAt(g4.Player, Hand(g4, "WCG-271"), u3));
        Assert.Equal(14, u3.CurrentPP);
        // 備戰可打怪物不能打玩家
        var enemy = Unit(g4.Computer, "WCG-101", 0);
        Assert.True(g4.Attack(g4.Player, u1, enemy));
        Assert.DoesNotContain(enemy, g4.Computer.Field);

        // 5. DamageCounters 版本旗標對 v2.2、v2.1、v1.x 與舊蓋牌反擊時代維持正確判定；非 V22 卡表（v2.1）維持舊 WCG-195 / WCG-196 判定
        var v22Engine = new GameEngine(cards, new Random(1));
        Assert.True(v22Engine.DamageCounters);
        Assert.True(v22Engine.V22Rules);
        var beforeV2 = CardBalanceHistory.BeforeV2(cards);
        Assert.True(new GameEngine(beforeV2, new Random(1)).DamageCounters);
        Assert.False(new GameEngine(beforeV2, new Random(1)).V22Rules);
        var beforeDc = CardBalanceHistory.BeforeDamageCounters(beforeV2);
        Assert.False(new GameEngine(beforeDc, new Random(1)).DamageCounters);

        var v21Db = new CardDatabase(
            System.Text.Json.JsonSerializer.Serialize(cards.AllCards.Select(c => c.Id == "WCG-022"
                ? new CardDefinition { Id = c.Id, Name = c.Name, Type = c.Type, Wills = c.Wills, CostSpec = c.CostSpec, CostGen = c.CostGen, TotalCost = c.TotalCost, PP = c.PP, DP = c.DP, Text = "看牌庫頂 3 張，選 1 張法術牌入手，其餘置底。", DesignNote = c.DesignNote, HsRef = c.HsRef, Keywords = c.Keywords, Arrows = c.Arrows }
                : c)),
            System.Text.Json.JsonSerializer.Serialize(cards.PresetDecks));
        var g195 = new GameEngine(v21Db, new Random(23));
        g195.StartGame(v21Db.PresetDecks[0], v21Db.PresetDecks[1], true);
        foreach (var p in new[] { g195.Player, g195.Computer }) { p.Hand.Clear(); p.Field.Clear(); p.Structures.Clear(); p.Graveyard.Clear(); p.EnergyZone.Clear(); for (int i = 0; i < 12; i++) p.EnergyZone.Add(new(v21Db.GetCard("WCG-101")!)); }
        Assert.True(g195.V21Rules);
        Assert.False(g195.V22Rules);
        var trap = new CardInstance(v21Db.GetCard("WCG-195")!); g195.Player.Hand.Add(trap);
        var bigFoe = new MonsterInstance(v21Db.GetCard("WCG-114")!) { Slot = 0, IsTapped = false, HasSummoningSickness = false }; g195.Computer.Field.Add(bigFoe);
        var foeHpBefore = g195.Computer.Hp;
        Assert.True(g195.EndTurn());
        while (g195.CurrentPendingChoice is { } ch195) Assert.True(g195.SelectChoice(ch195.Options.Last()));
        Assert.True(g195.Attack(g195.Computer, bigFoe));
        Choose(g195, o => o.Id == trap.InstanceId.ToString());
        Assert.DoesNotContain(bigFoe, g195.Computer.Field);
        Assert.Equal(foeHpBefore, g195.Computer.Hp);

        // 非 V22 卡表（v2.1）：WCG-196 不觸發於己方傷害（WCG-369 進場自傷）
        var g196Old = new GameEngine(v21Db, new Random(23));
        g196Old.StartGame(v21Db.PresetDecks[0], v21Db.PresetDecks[1], true);
        foreach (var p in new[] { g196Old.Player, g196Old.Computer }) { p.Hand.Clear(); p.Field.Clear(); p.Structures.Clear(); p.Graveyard.Clear(); p.EnergyZone.Clear(); for (int i = 0; i < 12; i++) p.EnergyZone.Add(new(v21Db.GetCard("WCG-101")!)); }
        var b196Old = new CardInstance(v21Db.GetCard("WCG-196")!); g196Old.Player.Hand.Add(b196Old);
        var z369Old = new CardInstance(v21Db.GetCard("WCG-369")!); g196Old.Player.Hand.Add(z369Old);
        Assert.True(g196Old.SummonMonster(g196Old.Player, z369Old));
        Choose(g196Old, o => o.Id == "SLOT:0");
        Assert.False(g196Old.IsWaiting);
        Assert.Equal(PlayerState.MaxHp - 1, g196Old.Player.Hp);
        Assert.Contains(b196Old, g196Old.Player.Hand);
    }

    [Fact]
    public void V22RulesBehaviorTests()
    {
        // 1. 免費召喚觸發進場（手牌、墓地、反擊 197、連鎖不無限迴圈）
        var g1 = Game();
        var s102 = Hand(g1, "WCG-102"); // 1 費進場抉擇
        var summon47 = Hand(g1, "WCG-047"); // 進場免費召喚 1 費怪
        Assert.True(g1.SummonMonster(g1.Player, summon47));
        Choose(g1, o => o.Id == "SLOT:0");
        Choose(g1, o => o.Id == s102.InstanceId.ToString());
        Choose(g1, o => o.Id == "SLOT:1");
        Assert.True(g1.IsWaiting); // WCG-102 免費進場依然觸發 Deploy 抉擇
        Choose(g1, o => o.Id == "LOOT");
        Choose(g1, o => o.Id == g1.Player.Hand[0].InstanceId.ToString());
        Assert.False(g1.IsWaiting);

        // 墓地召喚觸發進場 (WCG-194 喚醒 WCG-212 送墓 2)
        var gGrave = Game();
        var thief = new CardInstance(cards.GetCard("WCG-212")!);
        gGrave.Player.Graveyard.Add(thief);
        var deckBefore = gGrave.Player.Deck.Count;
        Assert.True(gGrave.SummonMonster(gGrave.Player, Hand(gGrave, "WCG-194")));
        Choose(gGrave, o => o.Id == "SLOT:0");
        Choose(gGrave, o => o.Id == thief.InstanceId.ToString());
        Choose(gGrave, o => o.Id == "SLOT:1");
        Assert.Equal(deckBefore - 2, gGrave.Player.Deck.Count);

        // 反擊 WCG-197 免費召喚生機怪也觸發進場（並連鎖免費召喚 WCG-101）
        var g197 = Game();
        var f197 = Hand(g197, "WCG-197");
        var nature47 = Hand(g197, "WCG-047"); // 生機 2 費：進場免費召喚 1 費怪
        var goblin = Hand(g197, "WCG-101"); // 1 費
        var eAtk = Unit(g197.Computer, "WCG-101", 0);
        Assert.True(g197.EndTurn());
        while (g197.CurrentPendingChoice is { } ch) Assert.True(g197.SelectChoice(ch.Options.Last()));
        Assert.True(g197.Attack(g197.Computer, eAtk));
        Choose(g197, o => o.Id == f197.InstanceId.ToString());
        Choose(g197, o => o.Id == nature47.InstanceId.ToString());
        Choose(g197, o => o.Id == "SLOT:0");
        Assert.True(g197.IsWaiting); // WCG-047 進場觸發免費召喚
        Choose(g197, o => o.Id == goblin.InstanceId.ToString());
        Choose(g197, o => o.Id == "SLOT:1");
        Assert.False(g197.IsWaiting);
        Assert.Equal(2, g197.Player.Field.Count);

        // 2. 檢索共用實作（022 檢索法術、192 檢索深淵怪＋送墓 2）
        var gTut = Game();
        gTut.Player.Deck.Clear();
        var spellTop = new CardInstance(cards.GetCard("WCG-004")!);
        gTut.Player.Deck.Add(new(cards.GetCard("WCG-101")!));
        gTut.Player.Deck.Add(spellTop);
        gTut.Player.Deck.Add(new(cards.GetCard("WCG-154")!));
        gTut.Player.Deck.Add(new(cards.GetCard("WCG-101")!));
        Assert.True(gTut.CastSpell(gTut.Player, Hand(gTut, "WCG-022")));
        Choose(gTut, o => o.Id == spellTop.InstanceId.ToString());
        while (gTut.CurrentPendingChoice is { } chB) Assert.True(gTut.SelectChoice(chB.Options[0]));
        Assert.Contains(spellTop, gTut.Player.Hand);
        Assert.Equal(3, gTut.Player.Deck.Count);

        // 3. 331 借刀之術：令 2 隻敵怪決鬥
        var g331 = Game();
        var e1 = Unit(g331.Computer, "WCG-101", 0); // 10 PP
        var e2 = Unit(g331.Computer, "WCG-114", 1); // 19 PP
        Assert.True(g331.CastSpell(g331.Player, Hand(g331, "WCG-331")));
        Assert.True(g331.SelectTarget(e1));
        Assert.True(g331.SelectTarget(e2));
        Assert.DoesNotContain(e1, g331.Computer.Field);
        Assert.Contains(e2, g331.Computer.Field);

        // 4. 一次判斷縮放 + 273 手牌變動即時更新 PP + 362 可自由放進空格
        var gScale = Game();
        var u228 = Unit(gScale.Player, "WCG-228", 0); // 18 + 9 = 27 PP
        Refresh(gScale);
        Assert.Equal(27, u228.CurrentPP);
        var u273 = Unit(gScale.Player, "WCG-273", 1); // 場上已有其他怪 => 228 失去 +9 => 18 PP
        Refresh(gScale);
        Assert.Equal(18, u228.CurrentPP);
        Assert.Equal(13, u273.CurrentPP); // 手牌 0 張
        for (int i = 0; i < 4; i++) Hand(gScale, "WCG-101");
        Assert.Equal(17, u273.CurrentPP); // 手牌 4 張 => 13 + 4 = 17 PP
        var sprout = Hand(gScale, "WCG-362");
        Assert.True(gScale.SummonMonster(gScale.Player, sprout));
        Choose(gScale, o => o.Id == "SLOT:4"); // 不在兩怪之間也可放
        Assert.Contains(gScale.Player.Field, m => m.Card.Id == "WCG-362" && m.Slot == 4);

        // 5. v2.2 WCG-195：抵銷傷害、消滅攻擊怪物、對敵方玩家造成 1 傷害，且該 1 傷害不能再被對方反擊
        var g195v22 = Game();
        var hpBefore = g195v22.Player.Hp;
        var foeHp = g195v22.Computer.Hp;
        var trap195 = Hand(g195v22, "WCG-195");
        var enemyBarrier = Hand(g195v22, "WCG-196", g195v22.Computer);
        var attacker = Unit(g195v22.Computer, "WCG-101", 0); // 10 PP，不依賴 PP 門檻
        Assert.True(g195v22.EndTurn());
        while (g195v22.CurrentPendingChoice is { } ch195) Assert.True(g195v22.SelectChoice(ch195.Options.Last()));
        Assert.True(g195v22.Attack(g195v22.Computer, attacker));
        Choose(g195v22, o => o.Id == trap195.InstanceId.ToString());
        Assert.Equal(hpBefore, g195v22.Player.Hp);
        Assert.DoesNotContain(attacker, g195v22.Computer.Field);
        Assert.Equal(foeHp - 1, g195v22.Computer.Hp);
        Assert.False(g195v22.IsWaiting);
        Assert.Contains(enemyBarrier, g195v22.Computer.Hand);

        // 6. v2.2 WCG-196：改為「受到傷害時」，自己造成的傷害（WCG-369 殉誓狂徒進場效果）也能抵銷並抽 1 張牌
        var g196v22 = Game();
        var barrier196 = Hand(g196v22, "WCG-196");
        var zealot369 = Hand(g196v22, "WCG-369");
        var playerHpBefore = g196v22.Player.Hp;
        Assert.True(g196v22.SummonMonster(g196v22.Player, zealot369));
        Choose(g196v22, o => o.Id == "SLOT:0");
        Assert.True(g196v22.IsWaiting);
        Choose(g196v22, o => o.Id == barrier196.InstanceId.ToString());
        Assert.False(g196v22.IsWaiting);
        Assert.Equal(playerHpBefore, g196v22.Player.Hp);
        Assert.Equal(2, g196v22.Player.Hand.Count); // WCG-369 抽 1 + WCG-196 抽 1

        // 7. v2.2 AI：召喚己方 WCG-369 自傷時不用 WCG-195 炸死自己怪物；敵方 1 血時優先用 WCG-195 斬殺
        var gAiSelf = Game();
        gAiSelf.Player.IsAi = true;
        gAiSelf.AiLevel = 5;
        Hand(gAiSelf, "WCG-195");
        var own369 = Hand(gAiSelf, "WCG-369");
        Assert.True(gAiSelf.SummonMonster(gAiSelf.Player, own369));
        while (gAiSelf.CurrentPendingChoice != null && gAiSelf.DecisionPlayerId == "player")
            Assert.True(gAiSelf.ExecuteAiStep());
        Assert.Contains(gAiSelf.Player.Field, m => m.Card.Id == "WCG-369");

        var gAiLethal = Game();
        gAiLethal.Player.IsAi = true;
        gAiLethal.AiLevel = 5;
        gAiLethal.Computer.Hp = 1;
        Hand(gAiLethal, "WCG-198");
        Hand(gAiLethal, "WCG-195");
        var foeAtk = Unit(gAiLethal.Computer, "WCG-101", 0);
        Assert.True(gAiLethal.EndTurn());
        while (gAiLethal.CurrentPendingChoice is { } chL) Assert.True(gAiLethal.SelectChoice(chL.Options.Last()));
        Assert.True(gAiLethal.Attack(gAiLethal.Computer, foeAtk));
        Assert.True(gAiLethal.ExecuteAiStep());
        Assert.True(gAiLethal.IsOver);
        Assert.True(gAiLethal.Computer.HasLost);
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
