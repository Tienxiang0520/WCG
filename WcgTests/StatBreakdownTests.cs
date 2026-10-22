using System.Reflection;
using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Models.Battle;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace WcgTests;

// PP/DP breakdown (GameEngine.StatBreakdown.cs): display only, every modifier named after its source card.
public class StatBreakdownTests
{
    private readonly CardDatabase cards;
    public StatBreakdownTests(){var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb"));var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(root);cards=new(env.Object);}
    private GameEngine Game(){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);g.Player.Hand.Clear();g.Computer.Hand.Clear();g.Player.Field.Clear();g.Computer.Field.Clear();return g;}
    private MonsterInstance Unit(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Field.Add(m);p.Field.Sort((a,b)=>a.Slot.CompareTo(b.Slot));return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void AddTurnBonus(MonsterInstance m,int amount,CardDefinition source)=>typeof(GameEngine).GetMethod("AddTurnBonus",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[m,amount,source]);
    private static void Consistent(MonsterInstance m,IReadOnlyList<StatSource> parts)
    {
        var sum=(m.Card.PP??0)+parts.Sum(x=>x.Amount);
        if(m.CurrentPP>0)Assert.Equal(m.CurrentPP,sum);else Assert.True(sum<=0); // PP never goes below 0
    }

    [Fact] public void UnmodifiedMonsterHasNoLines()
    {
        var g=Game();var m=Unit(g.Player,101,1);Refresh(g);
        Assert.Empty(g.PowerModifiers(m));Assert.Empty(g.DamageModifiers(m));
    }
    [Fact] public void ArrowAurasNameTheirSourceCard()
    {
        var g=Game();var ally=Unit(g.Player,101,1);var src=Unit(g.Player,49,2);var foe=Unit(g.Computer,154,3);var weakener=Unit(g.Computer,85,2);Refresh(g);
        // 049 points left/right at 101; 085 (enemy slot 2) points up at our slot 4-2=2 → src.
        var allyParts=g.PowerModifiers(ally);
        Assert.Equal([("WCG-049",200)],allyParts.Select(x=>(x.Source!.Id,x.Amount)));Assert.Equal(src.Card.Name,allyParts[0].Label);
        Consistent(ally,allyParts);
        var srcParts=g.PowerModifiers(src);Assert.Contains(srcParts,x=>x.Source?.Id=="WCG-085"&&x.Amount==-300);Consistent(src,srcParts);
        Consistent(foe,g.PowerModifiers(foe));Consistent(weakener,g.PowerModifiers(weakener));
    }
    [Fact] public void AttachmentsAreListedWithTheirName()
    {
        var g=Game();var m=Unit(g.Player,101,1);var banner=new CardInstance(cards.GetCard("WCG-221")!);m.Attachments.Add(new(banner,"player"));Refresh(g);
        var parts=g.PowerModifiers(m);
        Assert.Equal([("WCG-221",300)],parts.Select(x=>(x.Source!.Id,x.Amount)));Consistent(m,parts);
    }
    [Fact] public void TurnBonusSourcesAreTrackedAndResetWithTheBonus()
    {
        var g=Game();var m=Unit(g.Player,101,1);Refresh(g);var source=cards.GetCard("WCG-252")!;
        AddTurnBonus(m,300,source);AddTurnBonus(m,300,source);
        var parts=g.PowerModifiers(m);Assert.Equal([("WCG-252",600)],parts.Select(x=>(x.Source!.Id,x.Amount)));Consistent(m,parts);
        m.TurnBonus=0; // turn change (rules code resets only the number)
        Assert.Empty(g.PowerModifiers(m));
        AddTurnBonus(m,300,source);Assert.Equal([300],g.PowerModifiers(m).Select(x=>x.Amount));
    }
    [Fact] public void UntrackedBonusFallsBackToAGenericLine()
    {
        var g=Game();var m=Unit(g.Player,101,1);Refresh(g);m.CombatPowerBonus=500;m.NextCombatBonus=-500;
        var parts=g.PowerModifiers(m);Assert.Contains(parts,x=>x.Source==null&&x.Label=="戰鬥加成"&&x.Amount==500);
        Assert.Contains(parts,x=>x.Source==null&&x.Label=="下次戰鬥加成"&&x.Amount==-500);Consistent(m,parts);
        m.AttackDamageBonus=2;Assert.Equal([("攻擊加成",2)],g.DamageModifiers(m).Select(x=>(x.Label,x.Amount)));
    }
    [Fact] public void BreakdownMatchesCurrentPowerThroughoutSelfPlay()
    {
        int checkedUnits=0,modified=0;
        foreach(var g in AiSimulationTests.Positions(cards,matches:8,every:1))
            foreach(var m in g.Player.Field.Concat(g.Computer.Field))
            {var parts=g.PowerModifiers(m);Consistent(m,parts);checkedUnits++;if(parts.Count>0)modified++;
             Assert.Equal(m.CurrentDP,(m.Card.DP??1)+g.DamageModifiers(m).Sum(x=>x.Amount));}
        Assert.True(checkedUnits>200);Assert.True(modified>0);
    }
    [Fact] public void QueryingTheBreakdownNeverChangesTheMatch()
    {
        static string Play(CardDatabase cards,bool query)
        {
            var g=new GameEngine(cards,new Random(5)){AiLevel=4};g.SeedRankedAi(5);g.StartGame(cards.PresetDecks[1],cards.PresetDecks[3],true);g.Player.IsAi=true;
            for(int step=0;step<600&&!g.IsOver;step++)
            {
                if(query)foreach(var m in g.Player.Board.Concat(g.Computer.Board)){g.PowerModifiers(m);g.DamageModifiers(m);}
                if(!g.ExecuteAiStep())break;
            }
            return AiSimulationTests.Board(g);
        }
        Assert.Equal(Play(cards,false),Play(cards,true));
    }
    [Fact] public void SnapshotsWithoutModifiersOmitTheNewFields()
    {
        var card=new BattleCard(Guid.Empty,"WCG-101","x","怪物","秩序",1,1000,1,"","");
        var plain=new BattleMonster(card,1000,1,[],false,"",[],null);
        var json=JsonSerializer.Serialize(plain);
        Assert.DoesNotContain("PpParts",json);Assert.DoesNotContain("DpParts",json);
        var buffed=plain with{PpParts=[new("烽火戰旗","WCG-221",300)]};Assert.Contains("PpParts",JsonSerializer.Serialize(buffed));
    }
}
