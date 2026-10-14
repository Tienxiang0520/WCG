using WcgWeb.Models;
namespace WcgWeb.Services;

public partial class GameEngine
{
    private void TapKeywordEnergy(PlayerState p, MonsterInstance source, int count, string keyword)
    {
        foreach(var c in p.EnergyZone.Where(c=>!c.IsTapped).Take(count)) c.IsTapped=true;
        if(count==0)return;
        Present("pay",p,amount:count,label:"支付能量");
        Present("status",p,source.InstanceId,amount:count,card:source.Card,label:keyword);
        Log($"【{p.Name}】發動{keyword}，橫置 {count} 點能量。","action");
    }
    private void EnergyChoice(PlayerState p,MonsterInstance source,string keyword,string prefix,int max,Func<int,string> title,Action<int> apply)
    {
        Ask(p,keyword+"：選擇投入能量",Enumerable.Range(0,max+1).Select(n=>new ChoiceOption{Id=prefix+":"+n,Title=title(n)}),o=>apply(int.Parse(o.Id.AsSpan(prefix.Length+1))),sourceInstanceId:source.InstanceId);
        CurrentPendingChoice!.SourceCard=source.Card;
        CurrentPendingChoice.SourceMonster=source;
    }
    private void RageDecision(PlayerState p,MonsterInstance m)
    {
        if(!Alive(m)||!m.HasRage||p.AvailableEnergy==0)return;
        EnergyChoice(p,m,"怒擊","RAGE",p.AvailableEnergy,n=>n==0?"不投入，維持原本 DP":$"橫置 {n} 點能量，這次攻擊 DP＋{n}",n=>
        {
            if(!Alive(m)||!m.HasRage)return;
            n=Math.Min(n,p.AvailableEnergy);TapKeywordEnergy(p,m,n,"怒擊");m.AttackDamageBonus=n;
        });
    }
    private void WildDecision(PlayerState p,MonsterInstance m,MonsterInstance other)
    {
        if(!Alive(m)||!Alive(other)||!m.HasWild||p.AvailableEnergy==0)return;
        EnergyChoice(p,m,"野性","WILD",p.AvailableEnergy,n=>n==0?"不投入，維持原本 PP":$"橫置 {n} 點能量，這次交戰 PP＋{n*500}",n=>
        {
            if(!Alive(m)||!Alive(other)||!m.HasWild)return;
            n=Math.Min(n,p.AvailableEnergy);TapKeywordEnergy(p,m,n,"野性");m.CombatPowerBonus=n*500;
        });
    }
    private void Observe(PlayerState p)
    {
        var m=p.Field.FirstOrDefault(x=>x.HasObservation);
        var max=Math.Min(p.AvailableEnergy/2,p.Deck.Count);
        if(m==null||max==0)return;
        EnergyChoice(p,m,"觀測","OBSERVE",max,n=>n==0?"不觀測":$"橫置 {n*2} 點能量，觀測 {n} 次",n=>
        {
            if(!p.Field.Any(x=>x.HasObservation))return;
            n=Math.Min(n,Math.Min(p.AvailableEnergy/2,p.Deck.Count));TapKeywordEnergy(p,m,n*2,"觀測");
            // Each search and bottom placement finishes before the next top pair is inspected.
            Resolve(Enumerable.Range(0,n).Select<int,Action>(_=>()=>Search(p,2,_=>true)).ToArray());
        });
    }
    private void SoulReturn(PlayerState p,MonsterInstance m,CardInstance grave)
    {
        if(p.AvailableEnergy<2||!p.Graveyard.Contains(grave))return;
        Ask(p,"回魂：立即取回陣亡怪物？",[new(){Id="SOUL",Title="橫置 2 點能量，將這張牌加入手牌"},new(){Id="SKIP",Title="不回魂，留在墓地"}],o=>
        {
            if(o.Id!="SOUL"||p.AvailableEnergy<2||!p.Graveyard.Remove(grave))return;
            TapKeywordEnergy(p,m,2,"回魂");p.Hand.Add(grave);
            Present("recover",p,grave.InstanceId,card:grave.Card,label:"回魂取回");
        },sourceInstanceId:m.InstanceId);
        CurrentPendingChoice!.SourceCard=m.Card;
    }
    // Only new keyword windows enter this path; old catalogs retain the same AI decisions.
    private bool ChooseEnergyKeyword(PendingChoice choice)
    {
        var p=PlayerById(choice.OwnerId);var source=choice.SourceMonster;
        if(choice.Title=="回魂：立即取回陣亡怪物？")return SelectChoice(choice.Options.First(o=>o.Id=="SOUL"));
        string? prefix=choice.Title switch{"怒擊：選擇投入能量"=>"RAGE","野性：選擇投入能量"=>"WILD","觀測：選擇投入能量"=>"OBSERVE",_=>null};
        if(prefix==null)return false;
        var max=choice.Options.Max(o=>int.Parse(o.Id.AsSpan(prefix.Length+1)));var n=max;
        if(prefix=="OBSERVE")n=Math.Min(max,Math.Max(0,p.Deck.Count-1)); // Reserve the mandatory draw; empty-deck observation is optional.
        if(prefix=="RAGE"&&source!=null)
        {
            var e=GetOpponent(p);var lethal=Math.Max(0,e.Hp+GuardReduction(e,e.Hp+max)-source.CurrentDP);
            if(lethal<=max)n=lethal; // Leave spare energy when the same lethal needs fewer resources.
        }
        if(prefix=="WILD"&&source!=null&&LastAttack is {} attack)
        {
            var other=Player.Field.Concat(Computer.Field).FirstOrDefault(m=>m.InstanceId==(source.InstanceId==attack.AttackerId?attack.TargetId:attack.AttackerId));
            n=0;
            if(other!=null)
            {
                // Poison cannot be outpowered. Otherwise spend only enough to win the PP comparison.
                var needed=Math.Max(0,(other.CurrentPP-source.CurrentPP)/500+1);
                if(source.CurrentPP>other.CurrentPP)needed=0;
                if((!other.HasPoison || source.HasShield && needed<max) && needed<=max)n=needed;
                else if(!source.HasPoison && source.CurrentPP<other.CurrentPP)
                {
                    var tie=(other.CurrentPP-source.CurrentPP+499)/500;
                    if(tie<=max)n=tie; // Trade when surviving is impossible.
                }
            }
        }
        return SelectChoice(choice.Options.First(o=>o.Id==prefix+":"+n));
    }
    private AttackPreview CompareAiAttack(MonsterInstance a,MonsterInstance d)
    {
        if(!a.HasWild&&!d.HasWild)return CompareAttack(a,d);
        var ap=a.CombatPowerBonus;var dp=d.CombatPowerBonus;
        try{if(a.HasWild)a.CombatPowerBonus+=Owner(a).AvailableEnergy*500;if(d.HasWild)d.CombatPowerBonus+=Owner(d).AvailableEnergy*500;return CompareAttack(a,d);}
        finally{a.CombatPowerBonus=ap;d.CombatPowerBonus=dp;}
    }
}
