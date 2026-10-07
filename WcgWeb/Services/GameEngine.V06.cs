using WcgWeb.Models;
namespace WcgWeb.Services;

public partial class GameEngine
{
    private readonly Dictionary<Guid,int> plannedSlots = [];
    private int FirstSlot(PlayerState p) => Enumerable.Range(0,5).First(i => !p.Board.Any(m => m.Slot == i));
    private void ChooseSlot(PlayerState p, CardDefinition card, Action<int> action, bool cancel = false, Guid? source = null)
    {
        EnsureSlots(p);
        var slots = Enumerable.Range(0,5).Where(i => !p.Board.Any(m => m.Slot == i)).ToArray();
        if (slots.Length == 0) return;
        Ask(p, "選擇固定進場格位", slots.Select(i => new ChoiceOption { Id=$"SLOT:{i}", Title=$"第 {i+1} 格", Subtitle=card.Arrows.Length == 0 ? "進場後不能換位" : $"箭頭：{string.Join(" ",card.Arrows.Select(a=>a=="left"?"←":a=="right"?"→":"↑"))}，進場後不能換位" }), o => action(int.Parse(o.Id[5..])), cancel, source);
        if(CurrentPendingChoice != null) CurrentPendingChoice.SourceCard=card;
    }
    private void EnsureSlots(PlayerState p)
    {
        var used = new HashSet<int>();
        foreach (var m in p.Board) { if (m.Slot < 0 || !used.Add(m.Slot)) { m.Slot = Enumerable.Range(0,5).First(i => !used.Contains(i)); used.Add(m.Slot); } }
    }
    private void RefreshBoard()
    {
        EnsureSlots(Player); EnsureSlots(Computer);
        foreach(var p in new[]{Player,Computer}) foreach(var m in p.Field)
        {
            var unit=m;
            unit.Power = () => Math.Max(0,unit.BasePP+unit.Attachments.Sum(a => a.Card.Card.Id == "WCG-135" ? 500 : a.Card.Card.Id == "WCG-141" ? 700 : 0)+unit.NextCombatBonus+unit.TurnBonus+
                (!unit.IsSilenced && unit.Card.Id == "WCG-009" ? GetOpponent(p).Field.Count*200 : 0)+
                (!unit.IsSilenced && unit.Card.Id == "WCG-182" ? p.Field.Count(x=>x!=unit && x.Card.Will=="狂怒")*300 : 0));
            unit.ShieldQualified = !unit.IsSilenced && (unit.Card.HasDivineShield || unit.Card.Id=="WCG-133" && p.Hp<=3)
                || unit.Attachments.Any(a=>a.Card.Card.Id is "WCG-062" or "WCG-074" or "WCG-189" or "WCG-198")
                || Player.Field.Concat(Computer.Field).Any(source => !source.IsSilenced && source.Card.Arrows.Length>0 && PointsAt(Owner(source),source,p,unit));
        }
    }
    private bool PointsAt(PlayerState sourceOwner, MonsterInstance source, PlayerState owner, MonsterInstance target) =>
        source.Card.Arrows.Any(a=>a switch { "left" => owner==sourceOwner && target.Slot==source.Slot-1,
            "right"=>owner==sourceOwner && target.Slot==source.Slot+1, "up"=>owner!=sourceOwner && target.Slot==4-source.Slot, _=>false });
    public int ActualCost(PlayerState p, CardDefinition card) => Math.Max(card.IsMonster && p.NextCreatureDiscount>0 ? 1 : 0,
        card.TotalCost-(card.Id=="WCG-160" ? 7-p.Hp : 0)-(card.IsMonster ? p.NextCreatureDiscount : 0));
    private void Attach(PlayerState p, CardInstance card, MonsterInstance target, int? expire = null)
    { if(!Alive(target)) return; p.Graveyard.Remove(card); target.Attachments.Add(new(card,p.Id,expire)); RefreshBoard(); }
    private void ReadyMonster(MonsterInstance m) { if(!Alive(m))return; m.IsTapped=false;m.HasAttacked=false;m.HasSummoningSickness=false; }
    private void SummonStructure(PlayerState p, CardInstance c)
    { var slot=plannedSlots.Remove(c.InstanceId,out var s)?s:FirstSlot(p);p.Structures.Add(new(c.Card){InstanceId=c.InstanceId,Slot=slot,IsTapped=false,HasSummoningSickness=false});Present("summon",p,c.InstanceId,card:c.Card,label:"結界進場"); }
    public bool SetCard(PlayerState p, CardInstance c) => Change(()=>
    { if(!Main(p)||!p.Hand.Contains(c)||p.Occupied>=5)return Fail("目前不能蓋牌。");ChooseSlot(p,c.Card,slot=>{p.Hand.Remove(c);p.Structures.Add(new(c.Card){InstanceId=c.InstanceId,Slot=slot,IsSet=true,IsTapped=false});Present("set",p,c.InstanceId,label:"背面蓋牌");},true,c.InstanceId);return true; });
    public bool CanActivate(PlayerState p, MonsterInstance m) => Main(p)&&p.Structures.Contains(m)&&!m.IsSet&&!m.IsTapped && (m.Card.Id switch
    { "WCG-125"=>p.Deck.Count>0,"WCG-126"=>true,"WCG-128"=>p.Field.Count>0,"WCG-149"=>p.Hand.Count>0,"WCG-151"=>p.Field.Concat(GetOpponent(p).Field).Any(x=>x.CurrentPP<=800),_=>false });
    public bool Activate(PlayerState p, MonsterInstance m)=>Change(()=>
    {
        if(!CanActivate(p,m))return Fail("此結界目前不能發動。");
        void Pay(Action effect){m.IsTapped=true;effect();}
        switch(m.Card.Id)
        {
            case "WCG-125": CardChoice(p,"選擇置底卡，其餘留在牌庫頂",p.Deck.Take(2),c=>Pay(()=>{p.Deck.Remove(c);p.Deck.Add(c);}));break;
            case "WCG-126":Pay(()=>p.NextCreatureDiscount++);break;
            case "WCG-128":PickMonster(p,"選擇犧牲怪物",p.Field,x=>Pay(()=>{Resolve(()=>DrawMany(p,1));KillBatch([x],"犧牲",false);}));break;
            case "WCG-149":CardChoice(p,"選擇置底手牌",p.Hand,c=>Pay(()=>{p.Hand.Remove(c);p.Deck.Add(c);DrawMany(p,1);}));break;
            case "WCG-151":PickMonster(p,"選擇 PP 800 以下怪物",p.Field.Concat(GetOpponent(p).Field).Where(x=>x.CurrentPP<=800),x=>Pay(()=>KillBatch([x],m.Card.Name)));break;
        }
        return true;
    });
    private void DestroyStructure(MonsterInstance m)
    { if(!Alive(m))return;var p=Owner(m);p.Structures.Remove(m);p.Graveyard.Add(new(m.Card){InstanceId=m.InstanceId});Present("death",p,m.InstanceId,card:m.IsSet?null:m.Card,label:"摧毀結界／蓋牌"); }
    private void Dispel(PlayerState p,bool structures=true)
    {
        var all=p.Field.Concat(GetOpponent(p).Field).Where(x=>x.Attachments.Count>0||x.IsSilenced);
        if(structures)all=all.Concat(p.Structures.Concat(GetOpponent(p).Structures).Where(x=>!x.IsSet));
        PickMonster(p,"選擇驅散目標",all,x=>{if(!x.IsUnit)DestroyStructure(x);else{ReleaseAttachments(x);x.IsSilenced=false;RefreshBoard();}});
    }
    private void FreeFaction(PlayerState p,int cost,string faction, bool grave=false, Action<MonsterInstance>? after=null)
    {
        if(p.Occupied>=5)return; var zone=grave?p.Graveyard:p.Hand;
        CardChoice(p,"選擇非付費召喚怪物",zone.Where(c=>c.Card.IsMonster&&c.Card.TotalCost<=cost&&(faction==""||c.Card.Will==faction)),c=>
            ChooseSlot(p,c.Card,slot=>{if(!zone.Remove(c))return;plannedSlots[c.InstanceId]=slot;Summon(p,c,false);var m=p.Field.FirstOrDefault(x=>x.InstanceId==c.InstanceId);if(m!=null)after?.Invoke(m);}),true);
    }
    private void Summoned(PlayerState p,MonsterInstance m)
    {
        foreach(var x in p.Field.Where(x=>!x.IsSilenced&&x.Card.Id=="WCG-190"&&x.TriggersThisTurn<2).ToArray())
            if(m.Card.Will=="秩序"){x.TriggersThisTurn++;Resolve(()=>Heal(p,1));}
    }
    private void NewSpellTriggers(PlayerState p,CardDefinition? spell)
    {
        if(spell==null)return;
        foreach(var x in p.Field.Where(x=>!x.IsSilenced&&x.Card.Id=="WCG-185"&&x.TriggersThisTurn<2).ToArray())
            if(spell.Will=="理智"){x.TriggersThisTurn++;_effects.AddLast(()=>{if(Alive(x))PickMonster(p,"法術聯動消滅",GetOpponent(p).Field.Where(m=>m.CurrentPP<=1000),m=>KillBatch([m],x.Card.Name));});}
        foreach(var x in p.Structures.Where(x=>!x.IsSet&&x.Card.Id=="WCG-153").ToArray())
            if(spell.TotalCost>=2){_effects.AddLast(()=>{var cards=p.Graveyard.Where(c=>c.Card.IsSpell&&c.Card.TotalCost==1).ToArray();if(cards.Length>0){var c=cards[_random.Next(cards.Length)];p.Graveyard.Remove(c);p.Hand.Add(c);}});}
    }
    private void ExtraDeploy(PlayerState p,MonsterInstance m)
    {
        var e=GetOpponent(p);switch(m.Card.Id)
        {
            case "WCG-130": Resolve(()=>DrawMany(p,1),()=>CardChoice(p,"選擇手牌置底",p.Hand,c=>{p.Hand.Remove(c);p.Deck.Add(c);}));break;
            case "WCG-134": Dispel(p);break;
            case "WCG-138":TapEnemyEnergy(e);break;
            case "WCG-145":Ask(p,"逆流法師抉擇",[new(){Id="DISPEL",Title="驅散附著"},new(){Id="READY",Title="轉直立己方怪物"}],o=>{if(o.Id=="DISPEL")Dispel(p,false);else PickMonster(p,"選擇橫置己方怪物",p.Field.Where(x=>x.IsTapped),ReadyMonster);});break;
            case "WCG-161": Resolve(()=>KillBatch(p.Field.Concat(e.Field).Where(x=>x!=m).ToArray(),m.Card.Name),()=>{foreach(var x in p.Structures.Concat(e.Structures).Where(x=>!x.IsSet).ToArray())DestroyStructure(x);foreach(var c in p.Hand.ToArray()){p.Hand.Remove(c);p.Graveyard.Add(c);}});break;
            case "WCG-162":Resolve(()=>Heal(p,3),()=>DrawMany(p,2));break;
            case "WCG-163":p.EnergyZone.ForEach(c=>c.IsTapped=false);PickMonster(p,"選擇敵怪回手",e.Field,Bounce);break;
            case "WCG-171":Heal(p,2);break;
            case "WCG-175":FreeFaction(p,5,"",true);break;
            case "WCG-179":Heal(p,7);break;
            case "WCG-181":if(p.Field.Any(x=>x!=m&&x.Card.Will=="狂怒"))foreach(var x in p.Field.Where(x=>x.Card.Will=="狂怒"))x.TurnBonus+=300;break;
            case "WCG-183":Search(p,3,c=>c.Card.IsSpell);break;
            case "WCG-187":if(p.Field.Any(x=>x!=m&&x.Card.Will=="生機"))FreeFaction(p,2,"生機");break;
            case "WCG-188":Heal(p,p.Field.Count(x=>x!=m&&x.Card.Will=="生機"));break;
            case "WCG-191":if(p.Field.Any(x=>x!=m&&x.Card.Will=="秩序"))Resolve(()=>PickMonster(p,"選擇 PP 2000 以上敵怪",e.Field.Where(x=>x.CurrentPP>=2000),x=>KillBatch([x],m.Card.Name)),()=>Heal(p,1));break;
            case "WCG-193":Ask(p,"可犧牲其他深淵怪物抽2張",[new(){Id="YES",Title="犧牲並抽牌"},new(){Id="SKIP",Title="不犧牲"}],o=>{if(o.Id=="YES")PickMonster(p,"選擇深淵祭品",p.Field.Where(x=>x!=m&&x.Card.Will=="深淵"),x=>{Resolve(()=>DrawMany(p,2));KillBatch([x],"犧牲",false);});});break;
            case "WCG-194":var ready=p.Field.Any(x=>x!=m&&x.Card.Will=="深淵");FreeFaction(p,3,"深淵",true,x=>{if(ready)ReadyMonster(x);});break;
        }
    }
    private void OptionalTarget(PlayerState p,string title,IEnumerable<MonsterInstance> candidates,Action<MonsterInstance> action)
    {var list=candidates.Where(Alive).ToArray();if(list.Length==0)return;
        Ask(p,title,list.Select(m=>new ChoiceOption{Id=m.InstanceId.ToString(),Title=m.Card.Name,PreviewCard=m.Card}).Append(new(){Id="SKIP",Title="不使用此可選效果"}),o=>{if(o.Id!="SKIP")action(list.First(m=>m.InstanceId.ToString()==o.Id));});}
    private void TapEnemyEnergy(PlayerState e){var c=e.EnergyZone.FirstOrDefault(c=>!c.IsTapped);if(c!=null)c.IsTapped=true;}
    private void TopEnergy(PlayerState p){if(p.Deck.Count==0)return;var c=p.Deck[0];p.Deck.RemoveAt(0);c.IsTapped=false;p.EnergyZone.Add(c);}
    private void ExtraSpell(PlayerState p,CardInstance c,MonsterInstance? t)
    {
        var e=GetOpponent(p);switch(c.Card.Id)
        {
            case "WCG-135": if(t!=null)Attach(p,c,t);break;
            case "WCG-137":case "WCG-184":Resolve(()=>{if(t!=null&&Alive(t)){if(t.IsTapped)Bounce(t);else t.IsTapped=true;}},()=>{if(c.Card.Id=="WCG-137"||p.Board.Any(x=>!x.IsSet&&x.Card.Will=="理智"))DrawMany(p,1);});break;
            case "WCG-139":var friendly=t!=null&&p.Field.Contains(t);Resolve(()=>{if(t!=null)Bounce(t);},()=>{if(friendly)DrawMany(p,1);});break;
            case "WCG-140":var owner=t==null?p:Owner(t);Resolve(()=>{if(t!=null)KillBatch([t],c.Card.Name);},()=>DrawMany(owner,1));break;
            case "WCG-141":if(t!=null){Attach(p,c,t,TurnNumber+(Owner(t)==ActivePlayer?2:1));ReadyMonster(t);}break;
            case "WCG-142":if(t!=null){Attach(p,c,t);t.IsTapped=true;}break;
            case "WCG-143":if(t!=null){ReadyMonster(t);t.NextCombatBonus+=500;}break;
            case "WCG-164":case "WCG-169":DrawMany(p,1);break;
            case "WCG-165":if(t!=null&&Alive(t))t.NextCombatBonus+=500;break;
            case "WCG-166":CardChoice(p,"選擇另一張手牌置底",p.Hand,x=>{p.Hand.Remove(x);p.Deck.Add(x);DrawMany(p,1);});break;
            case "WCG-167":if(t!=null)ReadyMonster(t);break;
            case "WCG-168":if(t!=null)t.NextCombatBonus-=500;break;
            case "WCG-170":Resolve(()=>KillBatch(e.Field.Where(x=>x.CurrentPP<=1500).ToArray(),c.Card.Name),()=>{if(e.Field.Count==0)SpellDamage(p,e,2);});break;
            case "WCG-173":Resolve(()=>{foreach(var x in p.Field.Concat(e.Field).ToArray())Bounce(x);},()=>DrawMany(p,2));break;
            case "WCG-176":Resolve(()=>KillBatch(p.Field.Concat(e.Field).Where(x=>x.CurrentPP>=1500).ToArray(),c.Card.Name),()=>Heal(p,2));break;
            case "WCG-180":SearchTwoKinds(p);break;
            case "WCG-186":Resolve(()=>Search(p,3,x=>x.Card.IsMonster&&x.Card.Will=="生機"),()=>{if(p.Structures.Any(x=>!x.IsSet&&x.Card.Will=="生機"))DrawMany(p,1);});break;
            case "WCG-189":Resolve(()=>Search(p,3,x=>x.Card.IsMonster&&x.Card.Will=="秩序"),()=>{if(p.Field.Any(x=>x.Card.Will=="秩序"))PickMonster(p,"選擇附著聖盾怪物",p.Field.Where(x=>x.IsSilenced||x.Card.Id!="WCG-152"),x=>Attach(p,c,x));});break;
            case "WCG-192":MillSearch(p);break;
            default:throw new InvalidOperationException($"未實作卡牌 {c.Card.Id}");
        }
    }
    private void SearchTwoKinds(PlayerState p)
    {var top=p.Deck.Take(3).ToList();p.Deck.RemoveRange(0,top.Count);Resolve(()=>CardChoice(p,"選擇狂怒怪物",top.Where(c=>c.Card.IsMonster&&c.Card.Will=="狂怒"),c=>{top.Remove(c);p.Hand.Add(c);}),()=>CardChoice(p,"選擇狂怒法術",top.Where(c=>c.Card.IsSpell&&c.Card.Will=="狂怒"),c=>{top.Remove(c);p.Hand.Add(c);}),()=>OrderBottom(p,top));}
    private void MillSearch(PlayerState p)
    {var top=p.Deck.Take(3).ToList();p.Deck.RemoveRange(0,top.Count);Resolve(()=>CardChoice(p,"選擇深淵怪物加入手牌",top.Where(c=>c.Card.IsMonster&&c.Card.Will=="深淵"),c=>{top.Remove(c);p.Hand.Add(c);}),()=>p.Graveyard.AddRange(top));}
}
