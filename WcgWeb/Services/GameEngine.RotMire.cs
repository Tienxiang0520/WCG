using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「腐沼蔓生」（生機＋深淵，WCG-382–390，2026-10-25）：用箭頭和附著在敵方怪物底下的卡，讓整排慢慢爛掉。
// 腐蝕 (2026-11-12: no counters): 383／386's ↑ arrows give -300 PP, and 382／387 (-300) and 388 (-600) attached under an enemy
// monster lower its PP. PP stops at 0 and reaching 0 does not destroy the monster by itself (only cards such as 386／390 do).
// Printed-PP checks ignore them. Silence and dispel send the attached cards to the graveyard; they leave play with the monster.
// Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string MireCorrodeTitle = "選擇附著此卡的敵方怪物";
    internal const string MireSporeTitle = "屍菇傳播者：另選 1 隻敵方怪物（本回合 PP -300）";
    internal const string MireHarvestTitle = "選擇消滅的敵方怪物（底下有 2 張以上你的附著卡或被 2 個以上你的箭頭指向）";

    // Your corrosion sources at the enemy: your arrows pointing at enemy monsters plus your cards attached under them.
    private int Corrosion(PlayerState p) => GetOpponent(p).Field.Where(x => x.IsUnit).Sum(x => YourArrowsAt(p, x) + YourCardsUnder(p, x));
    private bool Corroded(PlayerState you, MonsterInstance x) => YourArrowsAt(you, x) > 0 || YourCardsUnder(you, x) > 0;
    // Attach a card (in a graveyard) under 1 enemy monster.
    private void Corrode(PlayerState p, CardInstance card, CardDefinition source, Action? after = null)
    {
        var foes = GetOpponent(p).Field.Where(x => x.IsUnit).ToList();
        if (source.IsSpell) foes = SpellResolvePickable(p, foes);
        if (foes.Count == 0 || IsOver) { after?.Invoke(); return; }
        PickMonster(p, MireCorrodeTitle, foes, x => { AttachFromGraveyard(card, x, source); after?.Invoke(); });
    }
    // Monsters the source's arrows point at, on either side.
    private IEnumerable<MonsterInstance> PointedUnits(MonsterInstance source) =>
        Player.Field.Concat(Computer.Field).Where(x => x != source && x.IsUnit && PointsAt(Owner(source), source, Owner(x), x)).ToArray();

    private bool MireDeploy(PlayerState p, MonsterInstance m)
    {
        var e = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "WCG-383": break;
            case "WCG-387": foreach (var x in PointedUnits(m)) AddDpCards(p, x, m.Card); break;
            case "WCG-389":
                var n = Math.Min(3, Corrosion(p));
                if (n > 0) Damage(e, n, new(p, m.Card, m, "deploy"));
                break;
            case "WCG-390":
                foreach (var x in e.Field.Where(x => x.IsUnit).ToArray()) AddTurnBonus(x, -600, m.Card);
                RefreshBoard();
                KillBatch(e.Field.Where(x => x.IsUnit && x.CurrentPP <= 0).ToArray(), m.Card.Name);
                break;
            default: return SpringDeploy(p, m);
        }
        return true;
    }
    // 382: attach itself under 1 enemy monster. 387: the same, then another enemy monster gets -300 PP this turn.
    private bool MireDeath(PlayerState p, MonsterInstance m, CardInstance grave)
    {
        if (m.IsSilenced || m.Card.Id is not ("WCG-382" or "WCG-387")) return false;
        Present("trigger", p, m.InstanceId, card: m.Card, label: "陣亡能力");
        if (m.Card.Id == "WCG-382") Corrode(p, grave, m.Card);
        else
        {
            MonsterInstance? first = null;
            var foes = GetOpponent(p).Field.Where(x => x.IsUnit).ToList();
            if (foes.Count == 0 || IsOver) return true;
            PickMonster(p, MireCorrodeTitle, foes, x =>
            {
                first = x; AttachFromGraveyard(grave, x, m.Card);
                var rest = GetOpponent(p).Field.Where(y => y.IsUnit && y != first).ToList();
                if (rest.Count > 0 && !IsOver) PickMonster(p, MireSporeTitle, rest, y => { AddTurnBonus(y, -300, m.Card); RefreshBoard(); });
            });
        }
        return true;
    }
    // 383's ↑ arrow is all it does now; nothing happens at the end of the turn.
    private void MireEndTurn(PlayerState p, List<Action> effects) { }
    // 386 沼母孵巢: at the start of your turn, destroy the monster its ↑ arrow points at if its PP is 0.
    private void MireStartTurn(PlayerState p)
    {
        foreach (var s in p.Structures.Where(x => !x.IsSet && x.Card.Id == "WCG-386").ToArray())
            _effects.AddLast(() =>
            {
                if (!p.Structures.Contains(s) || IsOver) return;
                var dead = PointedUnits(s).Where(x => x.CurrentPP <= 0).ToArray();
                if (dead.Length == 0) return;
                Present("trigger", p, s.InstanceId, card: s.Card, label: s.Card.Name);
                KillBatch(dead, s.Card.Name);
            });
    }
    // 385 腐根獵手: +500 PP attacking a monster whose PP you lowered (your arrow points at it, or your card is under it).
    private int MireCombatPower(MonsterInstance m, MonsterInstance other, bool attacking) =>
        attacking && !m.IsSilenced && m.Card.Id == "WCG-385" && other.IsUnit && Alive(m) && Corroded(Owner(m), other) ? 500 : 0;

    private bool MireSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        var e = GetOpponent(p);
        switch (source.Card.Id)
        {
            case "WCG-384": foreach (var x in e.Field.Where(x => x.IsUnit).ToArray()) AddTurnBonus(x, -300, source.Card); break;
            case "WCG-388":
                var ripe = SpellResolvePickable(p, e.Field.Where(x => x.IsUnit && MireRipe(p, x)));
                if (ripe.Count > 0) PickMonster(p, MireHarvestTitle, ripe, x => KillBatch([x], source.Card.Name));
                else Corrode(p, source, source.Card);
                break;
            default: return SurgeSpell(p, source, t);
        }
        RefreshBoard();
        return true;
    }
    private bool MireRipe(PlayerState p, MonsterInstance x) => YourCardsUnder(p, x) >= 2 || YourArrowsAt(p, x) >= 2;
    private string MirePlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-384" or "WCG-388" when !GetOpponent(p).Field.Any(x => x.IsUnit) => "敵方場上沒有怪物。",
        _ => SurgePlayProblem(p, c)
    };

    // ── 天梯電腦 ──
    private double? MirePlayValue(PlayerState p, CardInstance c)
    {
        var foes = GetOpponent(p).Field.Where(x => x.IsUnit).ToList();
        switch (c.Card.Id)
        {
            case "WCG-384": return foes.Count == 0 ? -5 : foes.Count * 1.3 - 1;
            case "WCG-388":
                var ripe = foes.Where(x => MireRipe(p, x)).ToList();
                return ripe.Count > 0 ? ripe.Max(BodyValue) + 1.5 : foes.Count == 0 ? -5 : 1;
        }
        return SurgePlayValue(p, c);
    }
    // Corrode the monster closest to dying (fewest PP left).
    private static MonsterInstance? MireTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) => pending.Title switch
    {
        MireSporeTitle => legal.Where(x => x.CurrentPP > 0).OrderBy(x => x.CurrentPP).FirstOrDefault() ?? legal.FirstOrDefault(),
        MireCorrodeTitle => legal.Where(x => x.CurrentPP > 0).OrderBy(x => x.CurrentPP).FirstOrDefault() ?? legal.FirstOrDefault(),
        MireHarvestTitle => legal.OrderByDescending(x => x.CurrentPP).FirstOrDefault(),
        _ => SurgeTarget(p, pending, legal)
    };
}
