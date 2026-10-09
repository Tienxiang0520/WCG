using WcgWeb.Models;
namespace WcgWeb.Services;
public partial class GameEngine
{
    public record AttackPresentation(Guid AttackerId, Guid? TargetId, long ActionNumber);
    public AttackPresentation? LastAttack {get;private set;}
    public bool CanAttack(MonsterInstance m)=>Main(ActivePlayer)&&ActivePlayer.Field.Contains(m)&&!m.IsTapped&&!m.AttackLocked&&(m.IsSilenced||!m.Card.CannotAttack);
    public IReadOnlyList<MonsterInstance> GetAttackTargets(MonsterInstance a)
    {if(!CanAttack(a))return [];var all=OpponentPlayer.Field.ToArray();var taunts=all.Where(m=>Blocks(a,m)).ToArray();return taunts.Length>0?taunts:all;}
    public bool CanAttackPlayer(MonsterInstance a)=>CanAttack(a)&&!OpponentPlayer.Field.Any(m=>Blocks(a,m));
    // A taunt monster forces attacks onto taunts unless the attacker ignores it: 059 past PP 1300 or less, 178 past every taunt.
    private bool Blocks(MonsterInstance a,MonsterInstance m)=>m.IsTaunt&&(a.IsSilenced||(a.Card.Id!="WCG-059"||m.CurrentPP>1300)&&(a.Card.Id!="WCG-178"||LegacyCardRules));
    public record AttackPreview(bool AttackerDies,bool DefenderDies,bool AttackerShieldBreaks,bool DefenderShieldBreaks,int PlayerDamage);
    private AttackPreview CompareAttack(MonsterInstance a,MonsterInstance? d)
    {if(d==null)return new(false,false,false,false,a.CurrentDP);var ah=a.CurrentPP<=d.CurrentPP||d.HasPoison;var dh=d.CurrentPP<=a.CurrentPP||a.HasPoison;
        var ash=ah&&a.HasShield&&Owner(a).AvailableEnergy>0;var dsh=dh&&d.HasShield&&Owner(d).AvailableEnergy>0;
        return new(ah&&!ash,dh&&!dsh,ash,dsh,!ah||ash ? dh&&!dsh&&a.HasTrample&&a.CurrentPP-d.CurrentPP>=700?1:0:0);}
    public AttackPreview? PreviewAttack(MonsterInstance a,MonsterInstance? d=null)=>(d==null?CanAttackPlayer(a):GetAttackTargets(a).Contains(d))?CompareAttack(a,d):null;
    private class AttackContext{public bool Cancelled;}
    public bool Attack(PlayerState p,MonsterInstance a,MonsterInstance? d=null)=>Change(()=>
    {
        if(!Main(p)||!CanAttack(a)||(d==null?!CanAttackPlayer(a):!GetAttackTargets(a).Contains(d)))return Fail("攻擊目標或直立狀態不合法。");
        LastAttack=new(a.InstanceId,d?.InstanceId,Revision+1);Present("attack",p,a.InstanceId,d?.InstanceId,card:a.Card,label:"攻擊");
        var ctx=new AttackContext();var e=GetOpponent(p);
        Resolve(()=>{if(!a.IsSilenced&&a.Card.Id=="WCG-178")KillBatch(e.Field.Where(x=>x.IsTaunt).ToArray(),a.Card.Name);
            if(d?.IsTaunt==true)foreach(var priest in e.Field.Where(x=>!x.IsSilenced&&x.Card.Id=="WCG-071"))Heal(e,1);},
            ()=>{if(d==null&&Alive(a))Counter(e,a,ctx);},()=>{if(!ctx.Cancelled)ResolveAttack(p,a,d);},()=>FinishAttack(p,a));return true;
    });
    private void FinishAttack(PlayerState p,MonsterInstance a)
    {if(!Alive(a))return;var changed=!a.IsTapped;a.IsTapped=true;a.HasAttacked=true;
        // "Until its next attack or combat ends": any attack, including one on the player or one a counter negated, uses up the bonus.
        if(!LegacyCardRules)a.NextCombatBonus=0;
        if((changed||!LegacyCardRules)&&!a.IsSilenced&&a.Card.Id=="WCG-136")PickMonster(p,"攻擊後消滅小怪",GetOpponent(p).Field.Where(x=>x.CurrentPP<=500),x=>KillBatch([x],a.Card.Name));}
    private void ResolveAttack(PlayerState p,MonsterInstance a,MonsterInstance? d)
    {
        if(!Alive(a))return;var e=GetOpponent(p);if(d==null){Damage(e,a.CurrentDP);return;}if(!e.Field.Contains(d))return;
        var ap=a.CurrentPP;var dp=d.CurrentPP;var ah=ap<=dp||d.HasPoison;var dh=dp<=ap||a.HasPoison;bool asaved=false,dsaved=false;
        Resolve(()=>ShieldDecision(a,ah,b=>asaved=b),()=>ShieldDecision(d,dh,b=>dsaved=b),()=>
        {
            var dead=new List<MonsterInstance>();if(ah&&!asaved)dead.Add(a);if(dh&&!dsaved)dead.Add(d);
            var kills=dead.ToArray();
            Resolve(()=>{if(Alive(a)&&kills.Contains(d)&&a.HasTrample&&ap-dp>=700)Damage(e,1);},
                ()=>{foreach(var x in p.Structures.Where(x=>!x.IsSet&&x.Card.Id=="WCG-124").ToArray())if(kills.Contains(d)){Damage(e,1);}
                    foreach(var x in e.Structures.Where(x=>!x.IsSet&&x.Card.Id=="WCG-124").ToArray())if(kills.Contains(a)){Damage(p,1);}},
                ()=>Survived(p,a,d,false),()=>Survived(e,d,a,true),()=>{a.NextCombatBonus=0;d.NextCombatBonus=0;});
            KillBatch(dead,"交戰");
        });
    }
    private void Survived(PlayerState p,MonsterInstance m,MonsterInstance other,bool defending)
    {if(!Alive(m)||m.IsSilenced)return;if(m.Card.Id=="WCG-109")DrawMany(p,1);
        if(m.Card.Id=="WCG-029"&&Alive(other)){if(other.IsTapped)DrawMany(p,1);else other.IsTapped=true;}
        if(defending&&m.Card.Id=="WCG-144")Damage(GetOpponent(p),1);}
    private void ShieldDecision(MonsterInstance m,bool hit,Action<bool> finish)
    {
        var p=Owner(m);if(!hit||!m.HasShield||p.AvailableEnergy==0){finish(false);return;}
        Ask(p,$"【{m.Card.Name}】將被交戰消滅，橫置1點能量保命？",[new(){Id="SHIELD",Title="橫置1點能量使用聖盾"},new(){Id="SKIP",Title="不使用，接受消滅"}],o=>
        {var use=o.Id=="SHIELD"&&p.AvailableEnergy>0;if(use){p.EnergyZone.First(c=>!c.IsTapped).IsTapped=true;Present("status",p,m.InstanceId,card:m.Card,label:"橫置能量保命");}finish(use);});
    }
    private void Counter(PlayerState p,MonsterInstance attacker,AttackContext ctx)
    {
        var sets=p.Structures.Where(x=>x.IsSet).ToArray();if(sets.Length==0)return;
        Ask(p,"玩家被宣告攻擊，可翻開1張蓋牌",sets.Select(x=>new ChoiceOption{Id=x.InstanceId.ToString(),Title=$"翻開第{x.Slot+1}格蓋牌"}).Append(new(){Id="SKIP",Title="不翻開"}),o=>
        {
            if(o.Id=="SKIP")return;var m=sets.First(x=>x.InstanceId.ToString()==o.Id);p.Structures.Remove(m);var c=new CardInstance(m.Card){InstanceId=m.InstanceId};p.Graveyard.Add(c);
            Present("reveal",p,c.InstanceId,card:c.Card,label:"翻開蓋牌");if(!c.Card.IsCounter||!CanPayCost(p,c.Card,out var payment))return;
            foreach(var x in payment)x.IsTapped=true;var e=GetOpponent(p);var power=attacker.CurrentPP;
            switch(c.Card.Id)
            {
                case "WCG-195":Resolve(()=>KillBatch([attacker],c.Card.Name),()=>{if(power>=1500)SpellDamage(p,e,1);});break;
                case "WCG-196":ctx.Cancelled=true;attacker.IsTapped=true;DrawMany(p,1);break;
                case "WCG-197":ctx.Cancelled=true;FreeFaction(p,3,"生機");break;
                case "WCG-198":ctx.Cancelled=true;Resolve(()=>Heal(p,2),()=>PickMonster(p,"可附著聖盾",p.Field.Where(x=>x.IsSilenced||x.Card.Id!="WCG-152"),x=>Attach(p,c,x)));break;
                case "WCG-199":Resolve(()=>KillBatch([attacker],c.Card.Name),()=>{if(p.Graveyard.Any(x=>x.Card.IsMonster&&x.Card.Will=="深淵"))Recover(p,2,1);});break;
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
                if (enemy.Field.Count == 0) Damage(enemy, 1); else PickLowest(p, enemy.Field, x => KillBatch(new[] { x }, m.Card.Name));
            });
            if (m.Card.Id is "WCG-060" or "WCG-177") effects.Add(() => { if (Alive(m) && !m.IsSilenced) FreeHand(p, 2, 1); });
        }
        foreach(var unit in p.Field.ToArray()) foreach(var attachment in unit.Attachments.Where(a=>a.ExpireTurn==TurnNumber).ToArray())
            effects.Add(()=>{if(Alive(unit)&&unit.Attachments.Contains(attachment))KillBatch([unit],"狂亂血宴到期");});
        effects.Add(() =>
        {
            foreach(var unit in Player.Field.Concat(Computer.Field)){unit.TurnBonus=0;unit.TriggersThisTurn=0;}
            p.NextCreatureDiscount=0;
            CurrentTurnPlayerId = GetOpponent(p).Id; TurnNumber++; CurrentPhase = TurnPhase.MainPhase;
            Present("turn", ActivePlayer, label: $"第 {TurnNumber} 回合");
            ActivePlayer.ResetEnergyForOwnTurn(); foreach (var m in ActivePlayer.Board) m.ResetTurnState(); RefreshBoard();
            Draw(ActivePlayer); Log($"第 {TurnNumber} 回合：輪到【{ActivePlayer.Name}】行動。", "action");
        });
        Resolve(effects.ToArray()); return true;
    });
    // Ranked matches set AiLevel 0..5 for the tiered AI; practice and training keep the v0.6 trial AI.
    public bool ExecuteAiStep() => Change(()=>AiLevel>=0?ExecuteTieredAi():ExecuteV06Ai());
    public void ExecuteAiTurn(){for(int i=0;i<400&&DecisionPlayerId==Computer.Id&&!IsOver;i++)if(!ExecuteAiStep())break;}
}
