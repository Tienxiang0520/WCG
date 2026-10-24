using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「烽火守望」（秩序＋生機，WCG-292–300，2026-10-23）：怪物「直立待命」到你的回合結束就造成傷害或回復。
// 「直立」 = not tapped. Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    private static bool Upright(MonsterInstance m) => m.IsUnit && !m.IsTapped;

    // 你的回合結束時 (EndTurn queues these before the turn passes; each re-checks that the source is still upright).
    private void BeaconEndTurn(PlayerState p, List<Action> effects)
    {
        var e = GetOpponent(p);
        foreach (var m in p.Field.ToArray())
        {
            if (Active(m, "WCG-292")) effects.Add(() => { if (Alive(m) && Active(m, "WCG-292") && Upright(m)) { Present("trigger", p, m.InstanceId, card: m.Card, label: "烽火"); Damage(e, 1, new(p, m.Card, m, "passive")); } });
            if (Active(m, "WCG-293")) effects.Add(() => { if (Alive(m) && Active(m, "WCG-293") && Upright(m)) { Present("trigger", p, m.InstanceId, card: m.Card, label: "守望"); Heal(p, 1); } });
            if (Active(m, "WCG-298") && HasArrows(m)) effects.Add(() =>
            {
                if (!Alive(m) || !Active(m, "WCG-298")) return;
                var lit = ArrowTargets(p, m).Where(x => Owner(x) == p && Upright(x)).ToArray();
                if (lit.Length == 0) return;
                Present("trigger", p, m.InstanceId, card: m.Card, label: "烽火傳令");
                Resolve(lit.Select(x => (Action)(() => { if (Alive(x) && Upright(x)) Damage(e, 1, new(p, x.Card, x, "passive")); })).ToArray());
            });
        }
        foreach (var s in p.Structures.Where(x => !x.IsSet && x.Card.Id == "WCG-295").ToArray())
            effects.Add(() => { if (p.Structures.Contains(s) && p.Field.Count(Upright) >= 3) { Present("trigger", p, s.InstanceId, card: s.Card, label: "烽火塔樓"); Damage(e, 1, new(p, s.Card, s, "passive")); } });
    }
    // 296 不動古樹 (upright): your other upright monsters +200. 297 守夜祈禱 attached: +400 while upright.
    private IEnumerable<(CardDefinition Source, int Amount)> BeaconPowerSources(PlayerState p, MonsterInstance m)
    {
        if (!m.IsUnit) yield break;
        foreach (var x in VaultPowerSources(p, m)) yield return x;
        if (Upright(m))
        {
            foreach (var tree in p.Field.Where(x => x != m && Active(x, "WCG-296") && Upright(x))) yield return (tree.Card, 200);
            foreach (var a in m.Attachments.Where(a => a.Card.Card.Id == "WCG-297")) yield return (a.Card.Card, 400);
        }
    }
    private int BeaconPower(PlayerState p, MonsterInstance m) => BeaconPowerSources(p, m).Sum(x => x.Amount);

    private bool BeaconDeploy(PlayerState p, MonsterInstance m)
    {
        switch (m.Card.Id)
        {
            case "WCG-300":
                var n = Math.Min(3, p.Field.Count(x => x != m && Upright(x)));
                if (n > 0) Damage(GetOpponent(p), n, new(p, m.Card, m, "deploy"));
                break;
            default: return VaultDeploy(p, m);
        }
        return true;
    }
    private List<MonsterInstance>? BeaconSpellTargets(PlayerState p, CardDefinition c) => c.Id == "WCG-297" ? p.Field.Where(x => x.IsUnit).ToList() : HoofSpellTargets(p, c);
    private static bool BeaconNeedsTarget(string id) => id == "WCG-297" || HoofNeedsTarget(id);
    private bool BeaconSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        switch (source.Card.Id)
        {
            case "WCG-294":
                foreach (var m in p.Field.Where(x => x.IsUnit && x.IsTapped && !x.AttackLocked))
                { m.IsTapped = false; m.AttackBanned = true; Present("status", p, m.InstanceId, card: m.Card, label: "轉為直立"); }
                RefreshBoard(); Log($"【{p.Name}】的橫置怪物轉為直立（本回合不能攻擊）。", "action");
                break;
            case "WCG-297": if (t != null && Alive(t) && p.Field.Contains(t)) Attach(p, source, t); break;
            case "WCG-299":
                var n = Math.Min(3, p.Field.Count(Upright));
                if (n > 0) Heal(p, n);
                break;
            default: return HoofSpell(p, source, t) || VaultSpell(p, source);
        }
        return true;
    }

    // ── 天梯電腦 ──
    // An upright beacon is worth its coming end-of-turn effect, so attacking with it (tapping) costs that value.
    private double BeaconBodyValue(MonsterInstance m)
    {
        if (!m.IsUnit || m.IsSilenced || m.IsTapped) return 0;
        return m.Card.Id switch { "WCG-292" => 4, "WCG-293" => 2, "WCG-296" => 1.5, "WCG-298" => 1, _ => 0 }
            + m.Attachments.Count(a => a.Card.Card.Id == "WCG-297") * 1.5;
    }
    private double? BeaconPlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        switch (c.Card.Id)
        {
            case "WCG-294": return p.Field.Where(m => m.IsUnit && m.IsTapped && !m.AttackLocked).Sum(m => 1 + BeaconBodyValueIfUpright(m)) - 2;
            case "WCG-297": return p.Field.Any(m => m.IsUnit) ? 2 : -5;
            case "WCG-299": return Math.Min(3, p.Field.Count(Upright)) * (p.Hp <= 4 ? 3 : 1.2) - (p.Hp >= 7 ? 5 : 1.5);
            case "WCG-300": return Math.Min(3, p.Field.Count(Upright)) * (e.Hp <= 3 ? 6 : 4);
        }
        return VaultPlayValue(p, c);
    }
    private double BeaconBodyValueIfUpright(MonsterInstance m) { var t = m.IsTapped; m.IsTapped = false; var v = BeaconBodyValue(m); m.IsTapped = t; return v; }
    private MonsterInstance? BeaconTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) => HoofTarget(p, pending, legal);
    private MonsterInstance? ArchSpellTarget(PlayerState p, string id, IReadOnlyList<MonsterInstance> own) => id switch
    {
        "WCG-297" => own.OrderByDescending(m => (Upright(m) ? 3 : 0) + (m.Card.CannotAttack ? 2 : 0) + BodyValue(m)).FirstOrDefault(),
        _ => HoofSpellTarget(p, id, own)
    };
}
