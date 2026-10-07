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
    }
    private void Freeze(MonsterInstance m){if(Alive(m))m.IsTapped=true;}
    private void ReleaseShield(MonsterInstance m,bool all=true) { }
}
