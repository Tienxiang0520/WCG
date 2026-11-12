using WcgWeb.Models;
namespace WcgWeb.Services;
public partial class GameEngine
{
    private PlayerState PlayerById(string? id) => id == Player.Id ? Player : Computer;
    internal bool StackableShields { get; set; } = false;
    private bool CanShield(MonsterInstance m) => Alive(m) && m.IsUnit && (m.IsSilenced || m.Card.Id != "WCG-152");
    private void ReleaseAttachments(MonsterInstance m)
    {
        if(m.SilenceSpell is {} spell)PlayerById(m.SilenceOwnerId).Graveyard.Add(spell);
        m.SilenceSpell=null;m.SilenceOwnerId=null;
        foreach(var a in m.Attachments)PlayerById(a.OwnerId).Graveyard.Add(a.Card);
        m.Attachments.Clear();
        foreach(var a in m.ShieldCards)PlayerById(a.OwnerId).Graveyard.Add(a.Card);
        m.ShieldCards.Clear();
    }
    // Leaving play: attachments and 聖盾 cards go to their owners' graveyards, face-down cards back to their deck bottoms.
    private void LeavePlay(MonsterInstance m){ReleaseAttachments(m);ReturnAllUnder(m);}
    private void Silence(PlayerState p,CardInstance spell,MonsterInstance m)
    {
        if(!Alive(m)||m.IsSilenced)return;p.Graveyard.Remove(spell);m.SilenceSpell=spell;m.SilenceOwnerId=p.Id;m.IsSilenced=true;RefreshBoard();
        Present("status",Owner(m),m.InstanceId,card:m.Card,label:"沉默");
        SilenceStrip(m);
    }
    // 2026-10-24 沉默／驅散規則: silence strips what the monster carries, and a monster with 王座篡奪 under it goes back to its owner
    // when silenced or dispelled. Older catalogs keep the old behaviour so their replays stay deterministic.
    internal bool SilenceCounterRules => CardBalanceHistory.SilenceCountersCatalog(_cardDb);
    private void SilenceStrip(MonsterInstance m)
    {
        if(!SilenceCounterRules||!Alive(m))return;
        // DP cards and countdown cards go back to the deck bottom (nothing triggers); 聖盾 cards and corrosion attachments go to the graveyard.
        if(m.Under.Any(u=>u.Kind is UnderKind.Dp or UnderKind.Countdown)){ReturnAllUnder(m,UnderKind.Dp,UnderKind.Countdown);RefreshBoard();}
        if(DeckCardRules)
        {
            PurgeShieldCards(m,"沉默");
            foreach(var a in m.Attachments.Where(a=>CorrosionAttachment(a.Card.Card.Id)).ToArray()){m.Attachments.Remove(a);PlayerById(a.OwnerId).Graveyard.Add(a.Card);}
            RefreshBoard();
        }
        UnSoul(m);
    }
    // 王座篡奪 goes to the graveyard: the monster moves to the empty slot of its owner's row nearest its slot (owner's hand if none).
    // released = Dispel already put 王座篡奪 into the graveyard with the other attachments.
    private void UnSoul(MonsterInstance m,bool released=false)
    {
        if(!SilenceCounterRules||!Alive(m)||m.OwnerId==null)return;
        if(!released)
        {
            var spell=m.Attachments.FirstOrDefault(a=>a.Card.Card.Id=="WCG-335");if(spell==null)return;
            m.Attachments.Remove(spell);PlayerById(spell.OwnerId).Graveyard.Add(spell.Card);
        }
        var home=PlayerById(m.OwnerId);var ctrl=Owner(m);
        if(ReferenceEquals(home,ctrl)){m.OwnerId=null;RefreshBoard();return;}
        var free=Enumerable.Range(0,5).Where(i=>!RowTaken(home,i)).OrderBy(i=>Math.Abs(i-m.Slot)).ThenBy(i=>i).ToList();
        if(free.Count==0){Log($"【{m.Card.Name}】底下的王座篡奪進入墓地，擁有者場上沒有空格，回到手牌。","action");Bounce(m);return;}
        ctrl.Field.Remove(m);home.Field.Add(m);m.Slot=free[0];m.OwnerId=null;m.Turned=false;m.ControlUntil=-1;
        RefreshBoard();
        Present("status",home,m.InstanceId,card:m.Card,label:"王座篡奪進入墓地：控制權歸還");
        Log($"【{m.Card.Name}】底下的王座篡奪進入墓地，回到【{home.Name}】的控制。","action");
    }
    private void ReleaseShield(MonsterInstance m,bool all=true) { }
}
