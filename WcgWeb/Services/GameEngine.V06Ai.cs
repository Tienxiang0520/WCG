using WcgWeb.Models;
namespace WcgWeb.Services;
public partial class GameEngine
{
    private bool ExecuteV06Ai()
    {
        if(IsOver||CurrentPhase==TurnPhase.NotStarted||!PlayerById(DecisionPlayerId).IsAi)return false;
        var p=PlayerById(DecisionPlayerId);var e=GetOpponent(p);
        if(CurrentPendingChoice is {} choice)
        {
            var preferred=choice.Options.FirstOrDefault(o=>o.Id=="SHIELD");
            if(preferred!=null)return SelectChoice(preferred);
            if(choice.Title.Contains("固定進場格位"))
            {var card=choice.SourceCard;
                double Position(ChoiceOption o){var slot=int.Parse(o.Id[5..]);var score=0d;
                    if(card?.Arrows.Contains("right")==true)score+=p.Field.Any(m=>m.Slot==slot+1)?5:slot<4?1:-4;
                    if(card?.Arrows.Contains("left")==true)score+=p.Field.Any(m=>m.Slot==slot-1)?5:slot>0?1:-4;
                    // 147's up arrow shields the enemy; every other up arrow harms the monster straight ahead.
                    if(card?.Arrows.Contains("up")==true)score+=e.Field.Any(m=>m.Slot==4-slot)?(card.Id=="WCG-147"?-6:6):0;
                    score+=p.Field.Count(m=>m.Card.Arrows.Contains("right")&&m.Slot==slot-1||m.Card.Arrows.Contains("left")&&m.Slot==slot+1)*3;
                    return score;}
                return SelectChoice(choice.Options.OrderByDescending(Position).First());}
            if(choice.Title.Contains("翻開"))return SelectChoice(choice.Options.FirstOrDefault(o=>o.Id!="SKIP")??choice.Options[0]);
            var option=choice.Options.OrderByDescending(o=>o.Id=="KEEP"?100:o.Id=="SKIP"?-100:o.PreviewCard is {} c?(c.PP??0)/500d+c.TotalCost:0).First();
            return SelectChoice(option);
        }
        if(CurrentPendingTarget is {} target)
        {
            var all=p.Board.Concat(e.Board).Where(m=>target.Validator?.Invoke(m)!=false).ToArray();
            var friendly=target.Title.Contains("犧牲")||target.Title.Contains("附著聖盾")||target.Title.Contains("己方怪物");
            var selected=all.OrderByDescending(m=>(p.Board.Contains(m)==friendly?20:0)+(target.Title.Contains("犧牲")?-m.CurrentPP:m.CurrentPP)/500d).FirstOrDefault();
            return selected!=null&&SelectTarget(selected);
        }
        if(!Main(p))return false;
        var lethal=p.Field.Where(m=>CanAttackPlayer(m)&&m.CurrentDP>0).ToArray();if(lethal.Sum(m=>m.CurrentDP)>=e.Hp&&lethal.Length>0)return Attack(p,lethal[0]);
        var face=p.Field.FirstOrDefault(m=>CanAttackPlayer(m)&&m.CurrentDP>=e.Hp&&m.CurrentDP>0);if(face!=null)return Attack(p,face);
        if(!p.HasFilledEnergyThisTurn&&p.Hand.Count>0&&(p.Hand.Count>1||!p.Hand.Any(c=>CanPlayCard(p,c)))&&p.TotalEnergy<Math.Max(5,p.Hand.Max(c=>ActualCost(p,c.Card))))
        {var energy=p.Hand.OrderBy(c=>c.Card.IsCounter?10:0).ThenByDescending(c=>c.Card.TotalCost).First();return PlayEnergy(p,energy);}
        // Keep real reaction cards and energy for the opponent; never read opponent set identities.
        var reaction=p.Hand.FirstOrDefault(c=>c.Card.IsCounter&&p.AvailableEnergy>=c.Card.TotalCost);
        if(reaction!=null&&p.Occupied<5&&!p.Structures.Any(x=>x.IsSet))return SetCard(p,reaction);
        var active=p.Structures.FirstOrDefault(x=>CanActivate(p,x));if(active!=null)return Activate(p,active);
        var playable=p.Hand.Where(c=>CanPlayCard(p,c)).Where(c=>!(c.Card.Id=="WCG-019"&&p.Hp<=2||c.Card.Id=="WCG-165"&&p.Hp<=1||c.Card.Id=="WCG-085"&&p.Hp<=1))
            .OrderByDescending(c=>c.Card.IsMonster).ThenBy(c=>ActualCost(p,c.Card)).FirstOrDefault(c=>!p.Structures.Any(x=>x.IsSet)&&!p.Field.Any(m=>m.HasShield)||p.AvailableEnergy-ActualCost(p,c.Card)>=1||p.Hand.Count<3);
        if(playable!=null)return playable.Card.IsMonster?SummonMonster(p,playable):playable.Card.IsEnchantment?PlayEnchantment(p,playable):CastSpell(p,playable);
        foreach(var m in p.Field.Where(CanAttack).OrderByDescending(x=>x.CurrentDP).ToArray())
        {
            var victim=GetAttackTargets(m).FirstOrDefault(x=>{var preview=CompareAttack(m,x);return preview.DefenderDies&&!preview.AttackerDies;});
            if(victim!=null)return Attack(p,m,victim);
            if(CanAttackPlayer(m)&&m.CurrentDP>0)return Attack(p,m);
            victim=GetAttackTargets(m).FirstOrDefault(x=>m.HasPoison||m.CurrentPP==x.CurrentPP);if(victim!=null)return Attack(p,m,victim);
        }
        return EndTurn();
    }
    public bool PlayEnchantment(PlayerState p,CardInstance c)=>Change(()=>c.Card.IsEnchantment?BeginPlay(p,c):Fail("不是結界。"));
    private void SpellDamage(PlayerState source,PlayerState target,int amount)
    {if(source!=target&&target.Field.Any(x=>!x.IsTapped))amount-=target.Structures.Count(x=>!x.IsSet&&x.Card.Id=="WCG-127");Damage(target,Math.Max(0,amount));}
}
