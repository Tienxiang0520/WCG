using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「腐沼蔓生」（生機＋深淵，WCG-382–390，2026-10-25）：在敵方怪物上疊腐蝕指示物，讓整排慢慢爛掉。
// 腐蝕指示物 (physical counters on the monster): each one gives -300 PP and they stack; PP stops at 0 and reaching 0 does not
// destroy the monster by itself (only cards such as 386／388／390 do). Printed-PP checks ignore them. Silence and dispel
// remove them, and they leave play with the monster (a monster coming back is a new instance with none).
// Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string MireCorrodeTitle = "選擇放置腐蝕指示物的敵方怪物";
    internal const string MireBroodTitle = "沼母孵巢：選擇敵方怪物（已有 3 個以上腐蝕指示物就消滅它）";
    internal const string MireHarvestTitle = "選擇消滅的敵方怪物（有 2 個以上腐蝕指示物）";

    private void AddCorrosion(MonsterInstance m, CardDefinition source, int n = 1)
    {
        if (!Alive(m) || !m.IsUnit || n <= 0) return;
        m.CorrosionCounters += n; RefreshBoard();
        Present("status", Owner(m), m.InstanceId, card: source, label: n == 1 ? "放置 1 個腐蝕指示物" : $"放置 {n} 個腐蝕指示物");
        Log($"【{m.Card.Name}】獲得 {n} 個腐蝕指示物（PP {m.CurrentPP}）。", "action");
    }
    private static int Corrosion(PlayerState p) => p.Field.Where(x => x.IsUnit).Sum(x => x.CorrosionCounters);
    private void Corrode(PlayerState p, CardDefinition source, int n = 1, Action? after = null)
    {
        var foes = GetOpponent(p).Field.Where(x => x.IsUnit).ToList();
        if (foes.Count == 0 || IsOver) { after?.Invoke(); return; }
        PickMonster(p, MireCorrodeTitle, foes, x => { AddCorrosion(x, source, n); after?.Invoke(); });
    }
    // Monsters the source's arrows point at, on either side.
    private IEnumerable<MonsterInstance> PointedUnits(MonsterInstance source) =>
        Player.Field.Concat(Computer.Field).Where(x => x != source && x.IsUnit && PointsAt(Owner(source), source, Owner(x), x)).ToArray();

    private bool MireDeploy(PlayerState p, MonsterInstance m)
    {
        var e = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "WCG-383": Corrode(p, m.Card); break;
            case "WCG-387": foreach (var x in PointedUnits(m)) AddDpCounter(x, m.Card); break;
            case "WCG-389":
                var n = Math.Min(3, Corrosion(e));
                if (n > 0) Damage(e, n, new(p, m.Card, m, "deploy"));
                break;
            case "WCG-390":
                foreach (var x in e.Field.Where(x => x.IsUnit).ToArray()) AddCorrosion(x, m.Card, 2);
                RefreshBoard();
                KillBatch(e.Field.Where(x => x.IsUnit && x.CurrentPP <= 0).ToArray(), m.Card.Name);
                break;
            default: return false;
        }
        return true;
    }
    // 382: 1 counter on 1 enemy monster. 387: 2 counters divided among enemy monsters (one at a time).
    private bool MireDeath(PlayerState p, MonsterInstance m, CardInstance grave)
    {
        if (m.IsSilenced || m.Card.Id is not ("WCG-382" or "WCG-387")) return false;
        Present("trigger", p, m.InstanceId, card: m.Card, label: "陣亡能力");
        if (m.Card.Id == "WCG-382") Corrode(p, m.Card);
        else Corrode(p, m.Card, 1, () => Corrode(p, m.Card));
        return true;
    }
    // 383 腐霧散布者: at the end of your turn, 1 counter on the monster its ↑ arrow points at.
    private void MireEndTurn(PlayerState p, List<Action> effects)
    {
        foreach (var m in p.Field.Where(x => Active(x, "WCG-383")).ToArray())
            effects.Add(() =>
            {
                if (!Alive(m) || !Active(m, "WCG-383")) return;
                foreach (var x in PointedUnits(m)) { Present("trigger", p, m.InstanceId, card: m.Card, label: m.Card.Name); AddCorrosion(x, m.Card); }
            });
    }
    // 386 沼母孵巢: at the start of your turn, 1 counter on 1 enemy monster, or destroy it if it already has 3 or more.
    private void MireStartTurn(PlayerState p)
    {
        foreach (var s in p.Structures.Where(x => !x.IsSet && x.Card.Id == "WCG-386").ToArray())
            _effects.AddLast(() =>
            {
                var foes = GetOpponent(p).Field.Where(x => x.IsUnit).ToList();
                if (!p.Structures.Contains(s) || foes.Count == 0 || IsOver) return;
                Present("trigger", p, s.InstanceId, card: s.Card, label: s.Card.Name);
                PickMonster(p, MireBroodTitle, foes, x => { if (x.CorrosionCounters >= 3) KillBatch([x], s.Card.Name); else AddCorrosion(x, s.Card); });
            });
    }
    // 385 腐根獵手: +500 PP attacking a monster with a 腐蝕指示物.
    private static int MireCombatPower(MonsterInstance m, MonsterInstance other, bool attacking) =>
        attacking && !m.IsSilenced && m.Card.Id == "WCG-385" && other.IsUnit && other.CorrosionCounters > 0 ? 500 : 0;

    private bool MireSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        var e = GetOpponent(p);
        switch (source.Card.Id)
        {
            case "WCG-384": foreach (var x in e.Field.Where(x => x.IsUnit).ToArray()) AddCorrosion(x, source.Card); break;
            case "WCG-388":
                var ripe = e.Field.Where(x => x.IsUnit && x.CorrosionCounters >= 2).ToList();
                if (ripe.Count > 0) PickMonster(p, MireHarvestTitle, ripe, x => KillBatch([x], source.Card.Name));
                else Corrode(p, source.Card, 2);
                break;
            default: return false;
        }
        RefreshBoard();
        return true;
    }
    private string MirePlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-384" or "WCG-388" when !GetOpponent(p).Field.Any(x => x.IsUnit) => "敵方場上沒有怪物。",
        _ => ""
    };

    // ── 天梯電腦 ──
    private double? MirePlayValue(PlayerState p, CardInstance c)
    {
        var foes = GetOpponent(p).Field.Where(x => x.IsUnit).ToList();
        switch (c.Card.Id)
        {
            case "WCG-384": return foes.Count == 0 ? -5 : foes.Count * 1.3 - 1;
            case "WCG-388":
                var ripe = foes.Where(x => x.CorrosionCounters >= 2).ToList();
                return ripe.Count > 0 ? ripe.Max(BodyValue) + 1.5 : foes.Count == 0 ? -5 : 1;
        }
        return null;
    }
    // Corrode the monster closest to dying (fewest PP left); the brood destroys one with 3 or more first.
    private static MonsterInstance? MireTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) => pending.Title switch
    {
        MireBroodTitle => legal.Where(x => x.CorrosionCounters >= 3).OrderByDescending(x => x.CurrentPP).FirstOrDefault() ?? legal.OrderByDescending(x => x.CurrentPP).FirstOrDefault(),
        MireCorrodeTitle => legal.Where(x => x.CurrentPP > 0).OrderBy(x => x.CurrentPP).FirstOrDefault() ?? legal.FirstOrDefault(),
        MireHarvestTitle => legal.OrderByDescending(x => x.CurrentPP).FirstOrDefault(),
        _ => null
    };
}
