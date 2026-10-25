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
    }
    private void Silence(PlayerState p,CardInstance spell,MonsterInstance m)
    {
        if(!Alive(m)||m.IsSilenced)return;p.Graveyard.Remove(spell);m.SilenceSpell=spell;m.SilenceOwnerId=p.Id;m.IsSilenced=true;RefreshBoard();
        Present("status",Owner(m),m.InstanceId,card:m.Card,label:"沉默");
        SilenceStrip(m);
    }
    // 2026-10-24 沉默／驅散規則: silence removes DP counters, and a monster with a 奪魂指示物 (王座篡奪) goes back to its owner
    // when silenced or dispelled. Older catalogs keep the old behaviour so their replays stay deterministic.
    internal bool SilenceCounterRules => CardBalanceHistory.SilenceCountersCatalog(_cardDb);
    private void SilenceStrip(MonsterInstance m)
    {
        if(!SilenceCounterRules||!Alive(m))return;
        if(m.DpCounters>0){m.DpCounters=0;RefreshBoard();}
        // 腐蝕指示物 (腐沼蔓生, 2026-10-25): silence removes them too.
        if(m.CorrosionCounters>0){m.CorrosionCounters=0;RefreshBoard();}
        UnSoul(m);
    }
    // Remove the 奪魂指示物: the monster moves to the empty slot of its owner's row nearest its slot (owner's hand if none).
    private void UnSoul(MonsterInstance m)
    {
        if(!SilenceCounterRules||!Alive(m)||!m.SoulCounter||m.OwnerId==null)return;
        var home=PlayerById(m.OwnerId);var ctrl=Owner(m);m.SoulCounter=false;
        if(ReferenceEquals(home,ctrl)){m.OwnerId=null;RefreshBoard();return;}
        var free=Enumerable.Range(0,5).Where(i=>!RowTaken(home,i)).OrderBy(i=>Math.Abs(i-m.Slot)).ThenBy(i=>i).ToList();
        if(free.Count==0){Log($"【{m.Card.Name}】移除奪魂指示物，擁有者場上沒有空格，回到手牌。","action");Bounce(m);return;}
        ctrl.Field.Remove(m);home.Field.Add(m);m.Slot=free[0];m.OwnerId=null;m.Turned=false;m.ControlUntil=-1;
        RefreshBoard();
        Present("status",home,m.InstanceId,card:m.Card,label:"移除奪魂指示物：控制權歸還");
        Log($"【{m.Card.Name}】移除奪魂指示物，回到【{home.Name}】的控制。","action");
    }
    private void ReleaseShield(MonsterInstance m,bool all=true) { }
}
