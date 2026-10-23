using WcgWeb.Models;
namespace WcgWeb.Services;
public partial class GameEngine
{
    public record AttackPresentation(Guid AttackerId, Guid? TargetId, long ActionNumber);
    public AttackPresentation? LastAttack {get;private set;}
    public bool CanAttack(MonsterInstance m)=>Main(ActivePlayer)&&ActivePlayer.Field.Contains(m)&&!m.IsTapped&&!m.AttackLocked&&!Pinned(m)&&(m.IsSilenced||!m.Card.CannotAttack);
    public IReadOnlyList<MonsterInstance> GetAttackTargets(MonsterInstance a)
    {if(!CanAttack(a))return [];var all=OpponentPlayer.Field.ToArray();var taunts=all.Where(m=>Blocks(a,m)).ToArray();return taunts.Length>0?taunts:all;}
    public bool CanAttackPlayer(MonsterInstance a)=>CanAttack(a)&&!OpponentPlayer.Field.Any(m=>Blocks(a,m));
    // A taunt monster forces attacks onto taunts unless the attacker ignores it: 059 past PP 1800 or less (1100/1300 in older eras), 178/201/210/261 past every taunt.
    private int GaiaTauntLimit()=>CardBalanceHistory.CardFixesCatalog(_cardDb)?1800:Low(1300);
    private bool Blocks(MonsterInstance a,MonsterInstance m)=>m.IsTaunt&&(a.IsSilenced||a.Card.Id is not ("WCG-210" or "WCG-261")&&(a.Card.Id!="WCG-201"||!CardBalanceHistory.CardFixesCatalog(_cardDb)))&&(a.IsSilenced||(a.Card.Id!="WCG-059"||ThresholdPP(m)>GaiaTauntLimit())&&(a.Card.Id!="WCG-178"||LegacyCardRules));
    public record AttackPreview(bool AttackerDies,bool DefenderDies,bool AttackerShieldBreaks,bool DefenderShieldBreaks,int PlayerDamage,int PlayerDamagePreventable=0,int RageEnergy=0,int WildEnergy=0);
    // Fixed 201／203 bonuses only exist during the attack, so previews add them temporarily.
    private AttackPreview CompareAttack(MonsterInstance a,MonsterInstance? d)
    {
        if(AttackDpBonus(a)==0&&!FixedCombatBonus(a)&&(d==null||!FixedCombatBonus(d)))return CompareAttackCore(a,d);
        var ad=a.AttackDamageBonus;var ac=a.CombatPowerBonus;var dc=d?.CombatPowerBonus??0;
        try{if(d==null){a.AttackDamageBonus=AttackDpBonus(a);}else{if(FixedCombatBonus(a))a.CombatPowerBonus=500;if(FixedCombatBonus(d))d.CombatPowerBonus=500;}return CompareAttackCore(a,d);}
        finally{a.AttackDamageBonus=ad;a.CombatPowerBonus=ac;if(d!=null)d.CombatPowerBonus=dc;}
    }
    private AttackPreview CompareAttackCore(MonsterInstance a,MonsterInstance? d)
    {if(d==null)return new(false,false,false,false,a.CurrentDP,GuardReduction(GetOpponent(Owner(a)),a.CurrentDP),a.HasRage?Owner(a).AvailableEnergy:0);var ah=a.CurrentPP<=d.CurrentPP||d.HasPoison;var dh=d.CurrentPP<=a.CurrentPP||a.HasPoison;
        var ash=(ah||SimplifiedRules&&ShieldAnyCombatRules)&&a.HasShield&&ShieldReady(a);var dsh=(dh||SimplifiedRules&&ShieldCounterRules)&&d.HasShield&&ShieldReady(d);
        var damage=!ah||ash ? dh&&!dsh&&a.HasTrample&&(SimplifiedRules||a.CurrentPP-d.CurrentPP>=700)?1:0:0;
        var guardSurvives=GetOpponent(Owner(a)).Field.Any(m=>m.HasHeroGuard&&(m!=d||!dh||dsh));
        var preventable=SimplifiedRules?Math.Min(damage,OathGuards(GetOpponent(Owner(a))).Count(m=>m!=d||!dh||dsh)):guardSurvives?Math.Min(damage,GetOpponent(Owner(a)).AvailableEnergy):0;
        return new(ah&&!ash,dh&&!dsh,ash,dsh,damage,preventable,0,(a.HasWild?Owner(a).AvailableEnergy:0)+(d.HasWild?Owner(d).AvailableEnergy:0));}
    private int GuardReduction(PlayerState p,int damage)=>SimplifiedRules?Math.Min(damage,OathGuards(p).Length):p.Field.Any(m=>m.HasHeroGuard)?Math.Min(damage,p.AvailableEnergy):0;
    public AttackPreview? PreviewAttack(MonsterInstance a,MonsterInstance? d=null)=>(d==null?CanAttackPlayer(a):GetAttackTargets(a).Contains(d))?CompareAttack(a,d):null;
    private class AttackContext{public bool Cancelled;}
    public bool Attack(PlayerState p,MonsterInstance a,MonsterInstance? d=null)=>Change(()=>
    {
        if(!Main(p)||!CanAttack(a)||(d==null?!CanAttackPlayer(a):!GetAttackTargets(a).Contains(d)))return Fail("攻擊目標或直立狀態不合法。");
        LastAttack=new(a.InstanceId,d?.InstanceId,Revision+1);Present("attack",p,a.InstanceId,d?.InstanceId,card:a.Card,label:"攻擊");
        var ctx=new AttackContext();var e=GetOpponent(p);
        Resolve(()=>{if(!a.IsSilenced&&a.Card.Id=="WCG-178")KillBatch(e.Field.Where(x=>x.IsTaunt).ToArray(),a.Card.Name);
            if(d?.IsTaunt==true)foreach(var priest in e.Field.Where(x=>!x.IsSilenced&&x.Card.Id=="WCG-071"))Heal(e,1);},
            ()=>{if(!DamageCounters&&d==null&&Alive(a))Counter(e,a,ctx);},()=>{if(!ctx.Cancelled && Alive(a)) Resolve(
                ()=>{if(SimplifiedRules){if(d==null){a.AttackDamageBonus=AttackDpBonus(a);}else if(Alive(d)){if(FixedCombatBonus(a))a.CombatPowerBonus=500;if(FixedCombatBonus(d))d.CombatPowerBonus=500;}}
                    else if(d==null) RageDecision(p,a); else if(Alive(d)) WildDecision(p,a,d);},
                ()=>{if(!SimplifiedRules && d!=null && Alive(a)&&Alive(d)) WildDecision(e,d,a);},
                ()=>ResolveAttack(p,a,d));},()=>{a.AttackDamageBonus=0;a.CombatPowerBonus=0;if(d!=null)d.CombatPowerBonus=0;FinishAttack(p,a);});return true;
    });
    private void FinishAttack(PlayerState p,MonsterInstance a)
    {if(!Alive(a))return;var changed=!a.IsTapped;a.IsTapped=true;a.HasAttacked=true;
        // "Until its next attack or combat ends": any attack, including one on the player or one a counter negated, uses up the bonus.
        if(!LegacyCardRules)a.NextCombatBonus=0;
        if((changed||!LegacyCardRules)&&!a.IsSilenced&&a.Card.Id=="WCG-136")PickMonster(p,"攻擊後消滅小怪",GetOpponent(p).Field.Where(x=>ThresholdPP(x)<=Low(500)),x=>KillBatch([x],a.Card.Name));}
    private void ResolveAttack(PlayerState p,MonsterInstance a,MonsterInstance? d)
    {
        if(!Alive(a))return;var e=GetOpponent(p);if(d==null){Damage(e,a.CurrentDP,new(p,a.Card,a,"attack"));return;}if(!e.Field.Contains(d))return;
        var ap=a.CurrentPP;var dp=d.CurrentPP;var mirror=d.IsUnit&&!d.IsSilenced&&d.Card.Id=="WCG-258";var ah=ap<=dp||d.HasPoison;var dh=dp<=ap||a.HasPoison;bool asaved=false,dsaved=false;
        // 聖盾指示物: the defender spends 1 counter whenever it is attacked, even when it would survive anyway;
        // from 聖盾交戰必扣 the attacker does too.
        Resolve(()=>ShieldDecision(a,ah||ShieldAnyCombatRules,b=>asaved=b,ah),()=>ShieldDecision(d,dh||ShieldCounterRules,b=>dsaved=b,dh),()=>
        {
            var dead=new List<MonsterInstance>();if(ah&&!asaved)dead.Add(a);if(dh&&!dsaved)dead.Add(d);
            var kills=dead.ToArray();
            Resolve(()=>{if(Alive(a)&&kills.Contains(d)&&a.HasTrample&&(SimplifiedRules||ap-dp>=700))Damage(e,1,new(p,a.Card,a,"attack"));},
                ()=>{
                    var triggers = new List<Action>();
                    foreach(var x in p.Structures.Where(x=>!x.IsSet&&x.Card.Id=="WCG-124").ToArray())if(kills.Contains(d))triggers.Add(()=>Damage(e,1,new(p,x.Card,x,"passive")));
                    foreach(var x in e.Structures.Where(x=>!x.IsSet&&x.Card.Id=="WCG-124").ToArray())if(kills.Contains(a))triggers.Add(()=>Damage(p,1,new(e,x.Card,x,"passive")));
                    if(DamageCounters) Resolve(triggers.ToArray()); else foreach(var trigger in triggers)trigger();
                },
                ()=>Survived(p,a,d,false),()=>Survived(e,d,a,true),()=>{OathCombatWon(p,a,d);OathCombatWon(e,d,a);},()=>{if(mirror&&Alive(a))Bounce(a);},()=>{SpendCombatAttachments(a);SpendCombatAttachments(d);},()=>{a.NextCombatBonus=0;d.NextCombatBonus=0;a.CombatPowerBonus=0;d.CombatPowerBonus=0;});
            KillBatch(dead,"交戰");
        });
    }
    private void Survived(PlayerState p,MonsterInstance m,MonsterInstance other,bool defending)
    {if(!Alive(m)||m.IsSilenced)return;if(m.Card.Id is "WCG-109" or "WCG-265")DrawMany(p,1);
        if(m.Card.Id=="WCG-029"&&!m.Card.Text.StartsWith("進場",StringComparison.Ordinal)&&Alive(other)){if(other.IsTapped)DrawMany(p,1);else other.IsTapped=true;}
        if(defending&&m.Card.Id=="WCG-144")Damage(GetOpponent(p),1,new(p,m.Card,m,"passive"));}
    private void ShieldDecision(MonsterInstance m,bool hit,Action<bool> finish,bool lethal=true)
    {
        if(SimplifiedRules){ApplyShield(m,hit,finish,lethal);return;}
        var p=Owner(m);if(!hit||!m.HasShield||p.AvailableEnergy==0){finish(false);return;}
        Ask(p,$"【{m.Card.Name}】將被交戰消滅，橫置1點能量保命？",[new(){Id="SHIELD",Title="橫置1點能量使用能量護盾"},new(){Id="SKIP",Title="不使用，接受消滅"}],o=>
        {var use=o.Id=="SHIELD"&&p.AvailableEnergy>0;if(use){p.EnergyZone.First(c=>!c.IsTapped).IsTapped=true;Present("status",p,m.InstanceId,card:m.Card,label:"橫置能量保命");}finish(use);});
    }
    private void Counter(PlayerState p,MonsterInstance attacker,AttackContext ctx)
    {
        var sets=p.Structures.Where(x=>x.IsSet).ToArray();if(sets.Length==0)return;
        Ask(p,"玩家被宣告攻擊，可翻開1張蓋牌",sets.Select(x=>new ChoiceOption{Id=x.InstanceId.ToString(),Title=$"翻開第{x.Slot+1}格蓋牌"}).Append(new(){Id="SKIP",Title="不翻開"}),o=>
        {
            if(o.Id=="SKIP")return;var m=sets.First(x=>x.InstanceId.ToString()==o.Id);p.Structures.Remove(m);var c=new CardInstance(m.Card){InstanceId=m.InstanceId};p.Graveyard.Add(c);
            if(!c.Card.IsCounter){Present("reveal",p,c.InstanceId,card:c.Card,label:"翻開蓋牌");return;}
            if(!CanPayCost(p,c.Card,out var payment))
            {
                var label=$"費用不足，反擊未發動（需要 {ActualCost(p,c.Card)}，可用 {p.AvailableEnergy}）；蓋牌已送墓地。";
                Present("reveal",p,c.InstanceId,card:c.Card,label:label);Log(label,"action");return;
            }
            Present("reveal",p,c.InstanceId,card:c.Card,label:"翻開蓋牌");
            foreach(var x in payment)x.IsTapped=true;var e=GetOpponent(p);var power=attacker.CurrentPP;
            switch(c.Card.Id)
            {
                case "WCG-195":Resolve(()=>KillBatch([attacker],c.Card.Name),()=>{if(power>=1500)SpellDamage(p,e,1,c.Card);});break;
                case "WCG-196":ctx.Cancelled=true;attacker.IsTapped=true;DrawMany(p,1);break;
                case "WCG-197":ctx.Cancelled=true;FreeFaction(p,3,"生機");break;
                case "WCG-198":ctx.Cancelled=true;Resolve(()=>Heal(p,2),()=>PickMonster(p,"可附著能量護盾",p.Field.Where(x=>x.IsSilenced||x.Card.Id!="WCG-152"),x=>Attach(p,c,x)));break;
                case "WCG-199":Resolve(()=>KillBatch([attacker],c.Card.Name),()=>{if(p.Graveyard.Any(x=>x.Card.IsMonster&&x.Card.HasWill("深淵")))Recover(p,2,1);});break;
            }
            UsedSpell(p,c.Card);
        });
    }
    public bool EndTurn() => Change(() =>
    {
        if (!Main(ActivePlayer)) return Fail("請先完成效果選擇，再結束回合。");
        var p = ActivePlayer; CurrentPhase = TurnPhase.EndingTurn; var effects = new List<Action>();
        foreach (var m in p.Field.ToArray())
        {
            if (m.IsSilenced) continue;
            if (m.Card.Id == "WCG-120") effects.Add(() =>
            {
                if (!Alive(m) || m.IsSilenced) return; var enemy = GetOpponent(p);
                if (enemy.Field.Count == 0) Damage(enemy, 1, new(p,m.Card,m,"passive")); else PickLowest(p, enemy.Field, x => KillBatch(new[] { x }, m.Card.Name));
            });
            if (m.Card.Id is "WCG-060" or "WCG-177") effects.Add(() => { if (Alive(m) && !m.IsSilenced) FreeHand(p, 2, 1); });
        }
        NewEndTurn(p, effects);
        foreach(var unit in p.Field.ToArray()) foreach(var attachment in unit.Attachments.Where(a=>a.ExpireTurn==TurnNumber).ToArray())
            effects.Add(()=>{if(Alive(unit)&&unit.Attachments.Contains(attachment))KillBatch([unit],"狂亂血宴到期");});
        effects.Add(() => Observe(GetOpponent(p)));
        effects.Add(() =>
        {
            foreach(var coin in p.EnergyZone.Where(x=>x.TemporaryEnergy).ToArray()){p.EnergyZone.Remove(coin);coin.TemporaryEnergy=false;coin.IsTapped=false;p.Graveyard.Add(coin);Present("discard",p,coin.InstanceId,card:coin.Card,label:"臨時能量消失");}
            var counters=ShieldCounterRules;
            foreach(var unit in Player.Field.Concat(Computer.Field)){unit.TurnBonus=0;unit.TriggersThisTurn=0;if(!counters)unit.ShieldSpent=false;}
            p.NextCreatureDiscount=0;
            CurrentTurnPlayerId = GetOpponent(p).Id; TurnNumber++; CurrentPhase = TurnPhase.MainPhase;
            Present("turn", ActivePlayer, label: $"第 {TurnNumber} 回合");
            ActivePlayer.ResetEnergyForOwnTurn(); foreach (var m in ActivePlayer.Board) m.ResetTurnState(); RefreshBoard();
            Draw(ActivePlayer); NewStartTurn(ActivePlayer); Log($"第 {TurnNumber} 回合：輪到【{ActivePlayer.Name}】行動。", "action");
        });
        Resolve(effects.ToArray()); return true;
    });
    // Ranked matches set AiLevel 0..5 for the tiered AI; practice and training keep the v0.6 trial AI.
    public bool ExecuteAiStep() => Change(()=>{var r=new Out<bool>();RunSlices(AiStepSlices(r));return r.Value;});
    public void ExecuteAiTurn(){for(int i=0;i<400&&DecisionPlayerId==Computer.Id&&!IsOver;i++)if(!ExecuteAiStep())break;}
}
