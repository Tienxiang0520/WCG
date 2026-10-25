using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 封時守衛（WCG-413）有倒數指示物時不能成為法術的目標：出牌時選目標、法術結算中選怪都要套用；
// 與禁魔石像（WCG-152）一致，不選目標的全場效果仍會影響它。
public class SpellWardTargetingTests
{
    private readonly CardDatabase cards;
    public SpellWardTargetingTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();p.Graveyard.Clear();for(int i=0;i<12;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private MonsterInstance Warden(GameEngine g,PlayerState p,int slot){var w=Unit(p,413,slot);w.Countdown=2;return w;}

    [Fact] public void SilenceSpell208CannotTargetTickingWardenOr152()
    {
        var g=Game();var w=Warden(g,g.Computer,0);var golem=Unit(g.Computer,152,1);var foe=Unit(g.Computer,113,2);Refresh(g);
        var spell=Hand(g,208);var targets=g.GetDirectPlayTargets(g.Player,spell);
        Assert.DoesNotContain(w,targets);Assert.DoesNotContain(golem,targets);Assert.Contains(foe,targets);
        Assert.False(g.CastSpellAt(g.Player,spell,w));Assert.False(w.IsSilenced);
        w.Countdown=0;Assert.Contains(w,g.GetDirectPlayTargets(g.Player,spell));
    }
    [Fact] public void Spell248SecondPickSkipsTickingWarden()
    {
        var g=Game();var own=Unit(g.Player,57,0);var w=Warden(g,g.Computer,0);var foe=Unit(g.Computer,101,1);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,248),own));
        var t=g.CurrentPendingTarget!;Assert.False(t.Validator!(w));Assert.True(t.Validator!(foe));
    }
    [Fact] public void Spell230FightPickSkipsTickingWarden()
    {
        var g=Game();var own=Unit(g.Player,57,0);var w=Warden(g,g.Computer,0);var foe=Unit(g.Computer,101,1);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,230),own));
        var t=g.CurrentPendingTarget!;Assert.False(t.Validator!(w));Assert.True(t.Validator!(foe));
    }
    [Fact] public void Spell412DrawsInsteadWhenOnlyTickingCardIsWarden()
    {
        var g=Game();var w=Warden(g,g.Player,0);Refresh(g);var hand=g.Player.Hand.Count;
        Assert.True(g.CastSpell(g.Player,Hand(g,412)));
        Assert.Null(g.CurrentPendingTarget);Assert.Equal(2,w.Countdown);Assert.Equal(hand+1,g.Player.Hand.Count);
    }
    [Fact] public void AreaSpell384StillCorrodesWarden()
    {
        var g=Game();var w=Warden(g,g.Computer,0);Refresh(g);
        Assert.True(g.CastSpell(g.Player,Hand(g,384)));Assert.True(w.CorrosionCounters>0);
    }
}
