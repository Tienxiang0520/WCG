using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新卡 205–257（設計 v2 第 5 節）：磨牌、箭頭站位、附著、手牌反擊、空格獎勵、犧牲與墓地降費、中立傭兵、反制與單色強化。
public class NewCardTests
{
    private readonly CardDatabase cards;
    public NewCardTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.EnergyZone.Clear();for(int i=0;i<12;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Card(int n)=>new(cards.GetCard($"WCG-{n:000}")!);
    private CardInstance Hand(GameEngine g,int n,PlayerState? p=null){var c=Card(n);(p??g.Player).Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Field.Add(m);p.Field.Sort((a,b)=>a.Slot.CompareTo(b.Slot));return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Choose(GameEngine g,Func<ChoiceOption,bool> pick)=>Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(pick)));
    private static void Slot(GameEngine g,int slot)=>Choose(g,o=>o.Id==$"SLOT:{slot}");
    private void Summon(GameEngine g,int n,int slot){Assert.True(g.SummonMonster(g.Player,Hand(g,n)));Slot(g,slot);}
    private static void EndTurnQuietly(GameEngine g){Assert.True(g.EndTurn());while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.Last()));}

    [Fact] public void CatalogHasAllDesignedCardsWithPlainText()
    {
        var added=cards.AllCards.Where(c=>int.Parse(c.Id[4..])>=205&&int.Parse(c.Id[4..])<258).ToArray();
        Assert.Equal(53,added.Length);Assert.Equal("WCG-257",added[^1].Id);Assert.Null(cards.GetCard("WCG-263"));
        Assert.Equal(["WCG-253","WCG-254","WCG-255","WCG-256","WCG-257"],added.Where(c=>c.IsVanilla).Select(c=>c.Id));
        Assert.All(added.Where(c=>c.IsVanilla),c=>{Assert.Equal("無。",c.Text);Assert.Empty(c.Keywords);});
        Assert.All(added,c=>{Assert.DoesNotContain("能量護盾",c.Text);Assert.All(c.Keywords,k=>Assert.Contains(k,new[]{"嘲諷","衝鋒","聖盾","貫穿","劇毒","沉默"}));
            foreach(var k in c.Keywords)Assert.Contains(k,c.Text);});
        Assert.Equal(["理智","深淵"],cards.GetCard("WCG-214")!.Wills);
        Assert.All(cards.PresetDecks,d=>Assert.True(d.IsValid(cards.GetCard,out _)));
    }
    // Design rule: any monster with an ability or keyword has PP below the same-cost vanilla curve.
    [Fact] public void AbilityMonstersStayBelowTheVanillaCurve()
    {
        var vanilla=new Dictionary<int,int>{[1]=700,[2]=1300,[3]=1600,[4]=2000,[5]=2300,[6]=2600,[7]=2900,[8]=3200,[9]=3500};
        var over=cards.AllCards.Where(c=>c.IsMonster&&c.Text.Trim() is not (""or"無"or"無。")&&vanilla.ContainsKey(c.TotalCost)&&(c.PP??0)>=vanilla[c.TotalCost]).Select(c=>c.Id).ToList();
        Assert.Empty(over);
    }
    // 寂靜禁咒 (WCG-208) is a spell attached under the silenced monster, so physical play needs no silence marker.
    [Fact] public void HushHexAttachesUnderTargetAndLeavesWithIt()
    {
        var hex=cards.GetCard("WCG-208")!;Assert.True(hex.IsSpell);Assert.Equal(2,hex.TotalCost);Assert.Equal("寂靜禁咒",hex.Name);Assert.True(hex.HasSilence);
        var g=Game();var mine=Unit(g.Player,101,0);var tree=Unit(g.Computer,49,0);Refresh(g);Assert.True(tree.IsTaunt);
        var spell=Hand(g,208);Assert.DoesNotContain(mine,g.GetDirectPlayTargets(g.Player,spell));
        Assert.True(g.CastSpellAt(g.Player,spell,tree));Assert.True(tree.IsSilenced);Assert.False(tree.IsTaunt);
        Assert.Same(spell,tree.SilenceSpell);Assert.DoesNotContain(spell,g.Player.Graveyard);
        Assert.False(g.CanPlayCard(g.Player,Hand(g,208)));
        var kill=typeof(GameEngine).GetMethod("KillBatch",BindingFlags.NonPublic|BindingFlags.Instance)!;kill.Invoke(g,[new[]{tree},"test",true]);
        Assert.Contains(spell,g.Player.Graveyard);
    }
    [Fact] public void DispelRemovesHushHexAndItsSilence()
    {
        var g=Game();var tree=Unit(g.Computer,49,0);var spell=Hand(g,208);Assert.True(g.CastSpellAt(g.Player,spell,tree));
        Assert.True(g.SummonMonster(g.Player,Hand(g,134)));
        for(int i=0;i<20&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id=="SLOT:1")??c.Options.First(o=>o.Id!="SKIP")));else Assert.True(g.SelectTarget(tree));}
        Assert.False(tree.IsSilenced);Assert.True(tree.IsTaunt);Assert.Null(tree.SilenceSpell);Assert.Contains(spell,g.Player.Graveyard);
    }
    [Fact] public void OldCatalogKeepsSilentScholarMonster()
    {
        var old=CounterHistory.BeforeSilenceSpell(cards);var scholar=old.GetCard("WCG-208")!;
        Assert.True(scholar.IsMonster);Assert.Equal("寂靜學者",scholar.Name);Assert.Equal(1400,scholar.PP);
        var g=new GameEngine(old,new Random(17));g.StartGame(old.PresetDecks[0],old.PresetDecks[1]);
        foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.EnergyZone.Clear();for(int i=0;i<12;i++)p.EnergyZone.Add(new(old.GetCard("WCG-101")!));}
        var tree=new MonsterInstance(old.GetCard("WCG-049")!){Slot=0,IsTapped=false,HasSummoningSickness=false};g.Computer.Field.Add(tree);
        var c=new CardInstance(scholar);g.Player.Hand.Add(c);Assert.True(g.CanPlayCard(g.Player,c));Assert.True(g.SummonMonster(g.Player,c));
        for(int i=0;i<20&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} ch)Assert.True(g.SelectChoice(ch.Options.FirstOrDefault(o=>o.Id=="SLOT:0")??ch.Options.First(o=>o.Id!="SKIP")));else Assert.True(g.SelectTarget(tree));}
        Assert.True(tree.IsSilenced);Assert.Contains(g.Player.Field,m=>m.Card.Id=="WCG-208");
    }
    [Fact] public void OldCatalogWithoutNewCardsIsPreserved()
    {
        var old=CounterHistory.BeforeNewCards(cards);
        Assert.Equal(204,old.AllCards.Count);Assert.Null(old.GetCard("WCG-205"));
        Assert.Equal(CounterHistory.BeforeAbilityPp(cards).AllCards.Take(204).Select(c=>System.Text.Json.JsonSerializer.Serialize(c)),old.AllCards.Select(c=>System.Text.Json.JsonSerializer.Serialize(c)));
        Assert.Equal(204,CounterHistory.BeforeNewCards(old).AllCards.Count);
    }

    // ---- A 記憶潮汐：磨牌 ----
    [Fact] public void MillPutsTopCardsIntoTheOpponentsGraveyardAndOnlyLosesOnDraw()
    {
        var g=Game();var deck=g.Computer.Deck.Count;var top=g.Computer.Deck.Take(3).ToArray();
        Assert.True(g.CastSpell(g.Player,Hand(g,211)));
        Assert.Equal(deck-3,g.Computer.Deck.Count);Assert.All(top,c=>Assert.Contains(c,g.Computer.Graveyard));
        Summon(g,212,0);Assert.Equal(deck-5,g.Computer.Deck.Count);
        // 214: X = monsters in your graveyard, at most 8.
        for(int i=0;i<10;i++)g.Player.Graveyard.Add(Card(101));
        deck=g.Computer.Deck.Count;Assert.True(g.CastSpell(g.Player,Hand(g,214)));Assert.Equal(deck-8,g.Computer.Deck.Count);
        // Milling an empty deck is harmless; the next draw loses.
        g.Computer.Deck.Clear();Assert.True(g.CastSpell(g.Player,Hand(g,211)));Assert.False(g.IsOver);
        Assert.True(g.EndTurn());Assert.True(g.Computer.HasLost);
    }
    [Fact] public void MemoryDevourerMillsAtEndOfTurnOnlyWhileUntapped()
    {
        var g=Game();var m=Unit(g.Player,213,0);var deck=g.Computer.Deck.Count;
        EndTurnQuietly(g);Assert.Equal(deck-2,g.Computer.Deck.Count); // mill 1 + the computer's draw
        EndTurnQuietly(g);m.IsTapped=true;deck=g.Computer.Deck.Count;EndTurnQuietly(g);Assert.Equal(deck-1,g.Computer.Deck.Count);
    }

    // ---- B 方陣列隊：箭頭站位 ----
    [Fact] public void RootwatchSentryHasTauntOnlyBetweenTwoAllies()
    {
        var g=Game();var s=Unit(g.Player,215,1);Unit(g.Player,101,0);Refresh(g);Assert.False(s.IsTaunt);
        Unit(g.Player,101,2);Refresh(g);Assert.True(s.IsTaunt);s.IsSilenced=true;Refresh(g);Assert.False(s.IsTaunt);
    }
    [Fact] public void VanguardStopsTheEnemyDirectlyAcrossFromAttacking()
    {
        var g=Game();Unit(g.Player,216,1);var across=Unit(g.Computer,154,3);var other=Unit(g.Computer,154,2);
        EndTurnQuietly(g);Assert.Equal("computer",g.CurrentTurnPlayerId);
        Assert.False(g.CanAttack(across));Assert.True(g.CanAttack(other));
    }
    [Fact] public void FormationCallDrawsPerArrowMonsterUpToThree()
    {
        var g=Game();Unit(g.Player,215,0);Unit(g.Player,216,1);Unit(g.Player,101,2);
        Assert.True(g.CastSpell(g.Player,Hand(g,217)));Assert.Equal(2,g.Player.Hand.Count);
        Unit(g.Player,21,3);Unit(g.Player,5,4);Assert.True(g.CastSpell(g.Player,Hand(g,217)));Assert.Equal(5,g.Player.Hand.Count);
    }
    [Fact] public void RootboundElderGivesAdjacentAlliesTrample()
    {
        var g=Game();var left=Unit(g.Player,101,0);Unit(g.Player,218,1);var right=Unit(g.Player,101,2);var far=Unit(g.Player,101,4);Refresh(g);
        Assert.True(left.HasTrample&&right.HasTrample);Assert.False(far.HasTrample);
    }
    [Fact] public void PhalanxCaptainBuffsOnlyWithAFullBoard()
    {
        var g=Game();Unit(g.Player,219,0);var a=Unit(g.Player,101,1);Unit(g.Player,101,2);Unit(g.Player,101,3);Refresh(g);Assert.Equal(700,a.CurrentPP);
        Structure(g.Player,231,4);Refresh(g);Assert.Equal(1000,a.CurrentPP);
    }

    // ---- C 戰紋巨獸：附著 ----
    [Fact] public void AttachmentsGrantTrampleAndAttackBonusesAndStack()
    {
        var g=Game();var lizard=Unit(g.Player,222,0);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,220),lizard));Assert.True(lizard.HasTrample);Assert.Equal(1800,lizard.CurrentPP);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,221),lizard));Assert.Equal(2600,lizard.CurrentPP);
        Assert.Equal(2,g.PreviewAttack(lizard)!.PlayerDamage);
        var hp=g.Computer.Hp;Assert.True(g.Attack(g.Player,lizard));Assert.Equal(hp-2,g.Computer.Hp);Assert.Equal(1,lizard.CurrentDP);
    }
    [Fact] public void RuneInheritorReturnsAnAttachedCardOnDeath()
    {
        var g=Game();var heir=Unit(g.Player,223,0);var rune=Hand(g,221);Assert.True(g.CastSpellAt(g.Player,rune,heir));
        var killer=Unit(g.Computer,114,2);Assert.True(g.Attack(g.Player,heir,killer));
        Assert.DoesNotContain(heir,g.Player.Field);Choose(g,o=>o.Id==rune.InstanceId.ToString());Assert.Contains(rune,g.Player.Hand);
    }

    // ---- D 預言之盾：手牌反擊 ----
    [Fact] public void JudgmentEchoCancelsMonsterDamageAndTapsIt()
    {
        var g=Game();Hand(g,225);EndTurnQuietly(g);var hp=g.Player.Hp;var foe=Unit(g.Computer,154,2);
        Assert.True(g.Attack(g.Computer,foe));Choose(g,o=>o.PreviewCard?.Id=="WCG-225");
        Assert.Equal(hp,g.Player.Hp);Assert.True(foe.IsTapped);Assert.Contains(g.Player.Graveyard,c=>c.Card.Id=="WCG-225");
    }
    [Fact] public void ForeseenDodgeOnlyAnswersSpellDamage()
    {
        var g=Game();Hand(g,226);EndTurnQuietly(g);var hp=g.Player.Hp;var hand=g.Player.Hand.Count;
        var foe=Unit(g.Computer,154,2);Assert.True(g.Attack(g.Computer,foe));Assert.Null(g.CurrentPendingChoice);Assert.Equal(hp-1,g.Player.Hp);
        Assert.True(g.CastSpell(g.Computer,Hand(g,14,g.Computer)));Choose(g,o=>o.PreviewCard?.Id=="WCG-226");
        Assert.Equal(hp-1,g.Player.Hp);Assert.Equal(hand,g.Player.Hand.Count); // the counter left the hand and 1 card was drawn
    }
    [Fact] public void StargazerTauntsWithAFullHandAndSentinelDrawsWhenAhead()
    {
        var g=Game();var star=Unit(g.Player,224,0);for(int i=0;i<4;i++)Hand(g,101);Refresh(g);Assert.False(star.IsTaunt);Hand(g,101);Refresh(g);Assert.True(star.IsTaunt);
        Hand(g,101,g.Computer);var before=g.Player.Hand.Count;Summon(g,227,1);Assert.Equal(before+1,g.Player.Hand.Count);
    }

    // ---- E 孤狼劍道：空格獎勵 ----
    [Fact] public void LoneSwordmasterGainsPowerPerEmptySlot()
    {
        var g=Game();var m=Unit(g.Player,228,0);Refresh(g);Assert.Equal(1900+4*300,m.CurrentPP);
        Unit(g.Player,101,1);Structure(g.Player,231,2);Refresh(g);Assert.Equal(1900+2*300,m.CurrentPP);
    }
    // 2026-10-17 理智加強: enemy spells can never target it, alone or not; its owner's spells still can.
    [Fact] public void VoidHermitCannotBeTargetedByEnemySpells()
    {
        var g=Game();var hermit=Unit(g.Computer,229,0);var kill=Hand(g,168);
        Assert.Empty(g.GetDirectPlayTargets(g.Player,kill));Assert.False(g.CanPlayCard(g.Player,kill));
        Unit(g.Computer,154,1);Assert.DoesNotContain(hermit,g.GetDirectPlayTargets(g.Player,kill));
        var own=Game();var mine=Unit(own.Player,229,0);Assert.Contains(mine,own.GetDirectPlayTargets(own.Player,Hand(own,168)));
    }
    [Fact] public void DuelBypassesTauntAndTapsTheChallenger()
    {
        var g=Game();var champ=Unit(g.Player,114,0);Unit(g.Computer,65,1);var back=Unit(g.Computer,154,2);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,230),champ));Assert.True(g.SelectTarget(back));
        Assert.DoesNotContain(back,g.Computer.Field);Assert.True(champ.IsTapped);Assert.Contains(champ,g.Player.Field);
    }
    // 2026-10-17 理智加強: draws while you control no more monsters than your opponent.
    [Fact] public void AltarOfEmptinessDrawsWhileYouHaveNoMoreMonstersThanTheOpponent()
    {
        var g=Game();Structure(g.Player,231,4);Unit(g.Player,101,0);Unit(g.Computer,101,0);EndTurnQuietly(g);var hand=g.Player.Hand.Count;EndTurnQuietly(g);Assert.Equal(hand+2,g.Player.Hand.Count);
        Unit(g.Player,101,1);EndTurnQuietly(g);hand=g.Player.Hand.Count;EndTurnQuietly(g);Assert.Equal(hand+1,g.Player.Hand.Count);
    }

    // ---- F 腐生循環：犧牲與墓地降費 ----
    [Theory][InlineData(55,true)][InlineData(154,false)]
    public void SacrificialSeedbedDrawsAndHealsForLifeSacrifices(int victimId,bool heals)
    {
        var g=Game();g.Player.Hp=4;var victim=Unit(g.Player,victimId,0);var hand=g.Player.Hand.Count;
        Assert.Equal("sacrifice",GameEngine.GetPlayPreparation(cards.GetCard("WCG-233")!));
        Assert.True(g.CastSpell(g.Player,Hand(g,233)));Assert.True(g.SelectTarget(victim));
        Assert.DoesNotContain(victim,g.Player.Field);Assert.Equal(hand+1,g.Player.Hand.Count);Assert.Equal(heals?5:4,g.Player.Hp);
        Assert.False(g.CanPlayCard(g.Player,Hand(g,233)));
    }
    [Fact] public void RotwoodColossusCostsLessForEveryThreeGraveyardMonsters()
    {
        var g=Game();var colossus=cards.GetCard("WCG-234")!;Assert.Equal(6,g.ActualCost(g.Player,colossus));
        for(int i=0;i<5;i++)g.Player.Graveyard.Add(Card(101));g.Player.Graveyard.Add(Card(14));Assert.Equal(5,g.ActualCost(g.Player,colossus));
        for(int i=0;i<30;i++)g.Player.Graveyard.Add(Card(101));Assert.Equal(2,g.ActualCost(g.Player,colossus));
    }
    [Fact] public void CorpseBloomRevivesAOneCostLifeMonsterAndSeedbedFillsEnergy()
    {
        var g=Game();var sprout=Card(cards.AllCards.First(c=>c.IsMonster&&c.HasWill("生機")&&c.TotalCost==1).Id is var id?int.Parse(id[4..]):0);g.Player.Graveyard.Add(sprout);
        var bloom=Unit(g.Player,235,0);var bed=Unit(g.Player,232,1);var killer=Unit(g.Computer,114,2);var energy=g.Player.EnergyZone.Count;
        Assert.True(g.Attack(g.Player,bloom,killer));Choose(g,o=>o.Id==sprout.InstanceId.ToString());Slot(g,0);
        Assert.Contains(g.Player.Field,m=>m.InstanceId==sprout.InstanceId);
        Assert.True(g.Attack(g.Player,bed,killer));Assert.Equal(energy+1,g.Player.EnergyZone.Count);
    }

    // ---- G 無旗傭兵：中立互相加成 ----
    [Fact] public void MercenariesRewardAllNeutralBoards()
    {
        var g=Game();Unit(g.Player,154,0);var hand=g.Player.Hand.Count;Summon(g,236,1);Assert.Equal(hand+1,g.Player.Hand.Count);
        Unit(g.Player,55,2);hand=g.Player.Hand.Count;Summon(g,236,3);Assert.Equal(hand,g.Player.Hand.Count);
        var general=Unit(g.Player,238,4);Refresh(g);Assert.Equal(2100+3*200,general.CurrentPP);
    }
    [Fact] public void MercenaryContractTakesOnlyANeutralMonster()
    {
        var g=Game();g.Player.Deck.InsertRange(0,[Card(55),Card(154),Card(14)]);var deck=g.Player.Deck.Count;
        Assert.True(g.CastSpell(g.Player,Hand(g,237)));
        Assert.Equal(["淡水狂鱷"],g.CurrentPendingChoice!.Options.Select(o=>o.Title));Choose(g,_=>true);
        while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options[0]));
        Assert.Equal("WCG-154",g.Player.Hand.Single().Card.Id);Assert.Equal(deck-1,g.Player.Deck.Count);
    }

    // ---- 5.1 反制 ----
    // 聖盾指示物: 205/209 remove the counter for good; it does not come back next turn.
    [Fact] public void ShieldBreakersRemoveTheHolyShieldCounter()
    {
        var g=Game();var wall=Unit(g.Computer,31,2);Refresh(g);Assert.True(wall.HasShield);Assert.False(wall.ShieldSpent);
        Summon(g,209,0);Assert.True(g.SelectTarget(wall));Assert.True(wall.ShieldSpent);
        var hitter=Unit(g.Player,154,1);Assert.True(g.PreviewAttack(hitter,wall)!.DefenderDies);
        EndTurnQuietly(g);Assert.True(wall.ShieldSpent);Assert.True(g.PreviewAttack(wall,hitter) is null or {AttackerShieldBreaks:false});
    }
    [Fact] public void TauntAnswersSilenceDestroyAndWalkPast()
    {
        var g=Game();var taunt=Unit(g.Computer,65,0);var big=Unit(g.Computer,114,1);big.Card=cards.GetCard("WCG-113")!;Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,206),taunt));Assert.True(taunt.IsSilenced);Assert.False(taunt.IsTaunt);
        var hp=g.Player.Hp;Assert.True(g.CastSpellAt(g.Player,Hand(g,207),big));Assert.Equal(hp-1,g.Player.Hp);Assert.DoesNotContain(big,g.Computer.Field);
        var wall=Unit(g.Computer,65,3);var climber=Unit(g.Player,210,0);Assert.True(g.CanAttackPlayer(climber));Assert.Contains(taunt,g.GetAttackTargets(climber));
        Assert.True(g.CastSpellAt(g.Player,Hand(g,208),wall));Assert.True(wall.IsSilenced);Assert.NotNull(wall.SilenceSpell);
    }

    // ---- 5.4 單色強化 ----
    [Fact] public void MonoWillBoosts()
    {
        var g=Game();var lava=Unit(g.Player,241,0);Refresh(g);Assert.Equal(2300,lava.CurrentPP);Unit(g.Player,101,1);Refresh(g);Assert.Equal(1900,lava.CurrentPP);
        var hp=g.Player.Hp;var hand=g.Player.Hand.Count;Assert.True(g.CastSpell(g.Player,Hand(g,242)));Assert.Equal(hand+2,g.Player.Hand.Count);Assert.Equal(hp-1,g.Player.Hp);
        var colossus=Unit(g.Player,252,2);Assert.True(g.CastSpell(g.Player,Hand(g,250)));Assert.Equal(2400,colossus.CurrentPP);
        EndTurnQuietly(g);Assert.Equal(2100,colossus.CurrentPP);
    }
    [Fact] public void FuryBannerAndRedScaleDealDamage()
    {
        var g=Game();Structure(g.Player,244,4);Unit(g.Player,243,0);var hp=g.Computer.Hp;
        EndTurnQuietly(g);Assert.Equal(hp-1,g.Computer.Hp);
        var guard=g.Player.Field.Single();var killer=Unit(g.Computer,114,2);Assert.True(g.Attack(g.Computer,killer,guard));Assert.Equal(hp-2,g.Computer.Hp);
    }
    [Fact] public void LifeBoostsDrawAndDestroy()
    {
        var g=Game();Unit(g.Player,55,0);Unit(g.Player,157,1);var hand=g.Player.Hand.Count;Summon(g,245,2);Assert.Equal(hand+1,g.Player.Hand.Count);
        Structure(g.Player,247,4);EndTurnQuietly(g);hand=g.Player.Hand.Count;EndTurnQuietly(g);Assert.Equal(hand+2,g.Player.Hand.Count);
        var small=Unit(g.Computer,101,0);var mid=Unit(g.Computer,114,1);var huge=Unit(g.Computer,155,2);huge.Card=cards.GetCard("WCG-253")!;huge.CurrentPP=2900;Refresh(g);
        Assert.DoesNotContain(mid,g.GetDirectPlayTargets(g.Player,Hand(g,246)));
        var croc=g.Player.Field.First(m=>m.Card.Id=="WCG-055");Assert.True(g.CastSpellAt(g.Player,Hand(g,248),croc));
        Assert.False(g.SelectTarget(huge));Assert.True(g.SelectTarget(small));Assert.DoesNotContain(small,g.Computer.Field);
    }
    [Fact] public void AbyssAndReasonBoosts()
    {
        var g=Game();var shade=Card(81);g.Player.Graveyard.Add(shade);var keeper=Unit(g.Player,249,0);var killer=Unit(g.Computer,114,2);
        Assert.True(g.Attack(g.Player,keeper,killer));Choose(g,o=>o.Id==shade.InstanceId.ToString());Assert.Contains(shade,g.Player.Hand);
        for(int i=0;i<5;i++)g.Player.Graveyard.Add(Card(101));var hand=g.Player.Hand.Count;Assert.True(g.CastSpell(g.Player,Hand(g,250)));Assert.Equal(hand+2,g.Player.Hand.Count);
        g=Game();Unit(g.Player,21,0);hand=g.Player.Hand.Count;Summon(g,251,1);Assert.Equal(hand+1,g.Player.Hand.Count);
    }
    [Fact] public void RankedAiValuesMillOnlyFromVersionSixSoOldJournalsReplayTheSame()
    {
        var g=Game();var mill=Hand(g,211,g.Computer);while(g.Player.Deck.Count>3)g.Player.Deck.RemoveAt(0);
        var rev=typeof(GameEngine).GetProperty("AiRevision",BindingFlags.NonPublic|BindingFlags.Instance)!;
        var value=typeof(GameEngine).GetMethod("NewPlayValue",BindingFlags.NonPublic|BindingFlags.Instance)!;
        Assert.Equal(GameEngine.RankedAiVersion,(int)rev.GetValue(g)!);Assert.Equal(10,GameEngine.RankedAiVersion);
        Assert.True((double)value.Invoke(g,[g.Computer,mill])! >= 50);   // milling the last cards wins
        rev.SetValue(g,5);Assert.Null(value.Invoke(g,[g.Computer,mill]));
    }
}
