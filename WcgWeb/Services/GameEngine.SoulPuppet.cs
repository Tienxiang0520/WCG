using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「奪魂傀儡」（深淵＋理智，WCG-328–336，2026-10-24）：把對手的怪物搶過來，用它打對手，用完就把它獻祭掉。
// Physical record: temporary control leaves the card in its owner's row turned 180° (Turned) and turns it back at the end
// of turn ControlUntil; 335 moves the card into an empty slot of yours with a 奪魂指示物 (SoulCounter). A monster you
// control but don't own (OwnerId set) leaves play to its owner's graveyard or hand. Only catalogs from that day contain
// these ids, and only they can steal, so older eras never reach this code.
public partial class GameEngine
{
    internal const string SoulSacrificeTitle = "選擇要犧牲的不是你擁有的怪物";
    internal const string SoulReturnTitle = "選擇移回擁有者手牌的怪物";

    private bool Foreign(PlayerState controller, MonsterInstance m) => m.OwnerId != null && m.OwnerId != controller.Id;
    private PlayerState HomeOf(MonsterInstance m, PlayerState controller) => m.OwnerId != null ? PlayerById(m.OwnerId) : controller;
    // A slot of p's row is taken by p's own cards there or by p's monsters the opponent controls in place (turned).
    private bool RowTaken(PlayerState p, int slot) => p.Board.Any(m => !m.Turned && m.Slot == slot) || GetOpponent(p).Field.Any(m => m.Turned && m.Slot == slot);
    // Monsters physically standing in p's row, whoever controls them.
    private IEnumerable<MonsterInstance> RowUnits(PlayerState p) => p.Field.Where(m => !m.Turned).Concat(GetOpponent(p).Field.Where(m => m.Turned));
    private PlayerState RowOf(MonsterInstance m) => m.Turned ? PlayerById(m.OwnerId) : Owner(m);

    private void TakeControl(PlayerState p, MonsterInstance m, int until, CardDefinition source, bool ready = false, bool noAttack = false)
    {
        if (!Alive(m) || !m.IsUnit) return;
        var from = Owner(m);
        if (ReferenceEquals(from, p)) return;
        from.Field.Remove(m); p.Field.Add(m);
        if (m.OwnerId == p.Id) { m.OwnerId = null; m.Turned = false; m.ControlUntil = -1; m.SoulCounter = false; }
        else { m.OwnerId ??= from.Id; if (!m.SoulCounter) m.Turned = true; m.ControlUntil = m.SoulCounter ? -1 : until; }
        if (ready) ReadyMonster(m);
        if (noAttack) m.AttackBanned = true;
        RefreshBoard();
        Present("status", p, m.InstanceId, card: source, label: m.Turned ? "取得控制權（轉 180 度）" : "取得控制權");
        Log($"【{p.Name}】取得【{m.Card.Name}】的控制權" + (m.Turned ? "（轉 180 度，留在原格）。" : "。"), "action");
    }
    private void ReturnControl(MonsterInstance m)
    {
        if (!Alive(m) || m.OwnerId == null) return;
        var ctrl = Owner(m); var home = PlayerById(m.OwnerId);
        if (ReferenceEquals(ctrl, home)) { m.OwnerId = null; m.Turned = false; return; }
        ctrl.Field.Remove(m); home.Field.Add(m);
        m.OwnerId = null; m.Turned = false; m.ControlUntil = -1; m.SoulCounter = false;
        RefreshBoard();
        Present("status", home, m.InstanceId, card: m.Card, label: "控制權歸還（轉回）");
        Log($"【{m.Card.Name}】回到【{home.Name}】的控制。", "action");
    }
    // End of every turn: monsters whose temporary control ends this turn turn back (tapped stays tapped).
    private void SoulReturnControl()
    {
        foreach (var m in Player.Field.Concat(Computer.Field).Where(x => x.Turned && x.ControlUntil <= TurnNumber).ToArray()) ReturnControl(m);
    }
    // 328 魂線操偶師: whenever you sacrifice a monster you don't own, draw 1.
    private void SoulSacrificed(PlayerState controller, MonsterInstance m, string reason)
    {
        if (!reason.Contains("犧牲") || !Foreign(controller, m)) return;
        foreach (var puppeteer in controller.Field.Where(x => x != m && Active(x, "WCG-328")).ToArray())
        {
            Present("trigger", controller, puppeteer.InstanceId, card: puppeteer.Card, label: puppeteer.Card.Name);
            DrawMany(controller, 1);
        }
    }
    // 334 血祭傀儡師: at the end of your turn you may sacrifice 1 monster you don't own to destroy 1 enemy monster with printed PP 1200 or less.
    private void SoulEndTurn(PlayerState p, List<Action> effects)
    {
        foreach (var m in p.Field.Where(x => Active(x, "WCG-334")).ToArray())
            effects.Add(() =>
            {
                if (!Alive(m) || !Active(m, "WCG-334") || !p.Field.Any(x => x.IsUnit && Foreign(p, x))) return;
                Ask(p, $"【{m.Card.Name}】可犧牲 1 隻不是你擁有的怪物", [new() { Id = "YES", Title = "犧牲並消滅 1 隻印刷 PP 1200 以下的敵方怪物" }, new() { Id = "SKIP", Title = "不犧牲" }], o =>
                {
                    if (o.Id != "YES") return;
                    PickMonster(p, SoulSacrificeTitle, p.Field.Where(x => x.IsUnit && Foreign(p, x)), x =>
                    {
                        Resolve(() => KillBatch([x], "犧牲", true),
                            () => PickMonster(p, "選擇印刷 PP 1200 以下的敵方怪物消滅", GetOpponent(p).Field.Where(y => y.IsUnit && (y.Card.PP ?? 0) <= 1200), y => KillBatch([y], m.Card.Name)));
                    });
                });
            });
    }
    private int UntilEndOfYourNextTurn(PlayerState p) => TurnNumber + (ReferenceEquals(ActivePlayer, p) ? 2 : 1);

    private bool SoulDeploy(PlayerState p, MonsterInstance m)
    {
        var e = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "WCG-333":
                if (!e.Field.Any(x => x.IsUnit)) DrawMany(p, 1);
                else PickMonster(p, "選擇印刷 PP 1200 以下的敵方怪物取得控制權", e.Field.Where(x => x.IsUnit && (x.Card.PP ?? 0) <= 1200), x => TakeControl(p, x, TurnNumber, m.Card, ready: true));
                break;
            default: return GraveDeploy(p, m);
        }
        return true;
    }
    private bool SoulDeath(PlayerState p, MonsterInstance m, CardInstance grave)
    {
        if (m.Card.Id != "WCG-332") return GraveDeath(p, m, grave);
        var e = GetOpponent(p);
        var small = e.Field.Where(x => x.IsUnit).ToList();
        if (small.Count == 0) return true;
        Present("trigger", p, m.InstanceId, card: m.Card, label: "陣亡能力");
        var low = small.Min(x => x.Card.PP ?? 0);
        PickMonster(p, "選擇印刷 PP 最低的敵方怪物取得控制權", small.Where(x => (x.Card.PP ?? 0) == low), x => TakeControl(p, x, UntilEndOfYourNextTurn(p), m.Card));
        return true;
    }

    private List<MonsterInstance>? SoulSpellTargets(PlayerState p, CardDefinition c)
    {
        var e = GetOpponent(p);
        return c.Id switch
        {
            "WCG-330" => e.Field.Where(x => x.IsUnit && (x.Card.PP ?? 0) <= 1200).ToList(),
            "WCG-331" => e.Field.Where(x => x.IsUnit && (x.Card.PP ?? 0) <= 1800).ToList(),
            "WCG-335" => e.Field.Where(x => x.IsUnit).ToList(),
            _ => GraveSpellTargets(p, c)
        };
    }
    private static bool SoulNeedsTarget(string id) => id is "WCG-330" or "WCG-331" or "WCG-335" || GraveNeedsTarget(id);
    private bool soulPactForeign;
    private bool SoulSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        var e = GetOpponent(p);
        switch (source.Card.Id)
        {
            case "WCG-329":
                if (soulPactForeign) Damage(e, 2, new(p, source.Card, null, "spell")); else DrawMany(p, 1);
                soulPactForeign = false;
                break;
            case "WCG-330":
                if (t == null || !Alive(t) || !e.Field.Contains(t)) break;
                if (!t.IsTapped) { t.IsTapped = true; Present("status", e, t.InstanceId, card: t.Card, label: "橫置"); }
                else TakeControl(p, t, TurnNumber, source.Card, noAttack: true);
                break;
            case "WCG-331":
                if (t != null && Alive(t) && e.Field.Contains(t)) TakeControl(p, t, TurnNumber, source.Card, ready: true);
                break;
            case "WCG-335":
                if (t == null || !Alive(t) || !e.Field.Contains(t) || p.Occupied >= 5) break;
                ChooseSlot(p, t.Card, slot => Usurp(p, t, slot, source.Card));
                break;
            case "WCG-336":
                var foreign = p.Field.Where(x => x.IsUnit && Foreign(p, x)).ToList();
                if (foreign.Count > 0) PickMonster(p, SoulReturnTitle, foreign, x => { Bounce(x); DrawMany(p, 2); });
                else PickMonster(p, "選擇移回手牌的己方怪物", p.Field.Where(x => x.IsUnit), x => { Bounce(x); DrawMany(p, 1); });
                break;
            default: return GraveSpell(p, source, t);
        }
        RefreshBoard();
        return true;
    }
    // 335 王座篡奪: the card moves into your empty slot keeping its tapped state, with a 奪魂指示物; you control it for good.
    // Like any monster that changes sides it can't attack the turn you take it.
    private void Usurp(PlayerState p, MonsterInstance t, int slot, CardDefinition source)
    {
        var e = Owner(t);
        if (!Alive(t) || ReferenceEquals(e, p) || RowTaken(p, slot)) return;
        e.Field.Remove(t); p.Field.Add(t);
        var owner = t.OwnerId ?? e.Id;
        t.Slot = slot; t.Turned = false; t.ControlUntil = -1;
        if (owner == p.Id) { t.OwnerId = null; t.SoulCounter = false; }
        else { t.OwnerId = owner; t.SoulCounter = true; }
        t.AttackBanned = true;
        RefreshBoard();
        Present("status", p, t.InstanceId, card: source, label: t.SoulCounter ? "放置奪魂指示物：永久控制" : "取得控制權");
        Log($"【{p.Name}】永久取得【{t.Card.Name}】的控制權。", "action");
    }
    private string SoulPlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-329" when !p.Field.Any(x => x.IsUnit) => "需要犧牲 1 隻己方怪物。",
        "WCG-335" when p.Occupied >= 5 => "你場上沒有空格。",
        "WCG-336" when !p.Field.Any(x => x.IsUnit) => "需要 1 隻己方怪物。",
        _ => GravePlayProblem(p, c)
    };

    // ── 天梯電腦 ──
    private bool SacrificeOutlet(PlayerState p) => p.Field.Any(x => Active(x, "WCG-334") || Active(x, "WCG-328")) || p.Hand.Any(x => x.Card.Id == "WCG-329");
    private double? SoulPlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        var foreign = p.Field.Where(x => x.IsUnit && Foreign(p, x)).ToList();
        double Steal(MonsterInstance x) => BodyValue(x) * .5 + x.CurrentDP * 1.2 + (x.IsTaunt ? 2.5 : 0) + (SacrificeOutlet(p) ? 1.5 : 0);
        switch (c.Card.Id)
        {
            case "WCG-329":
                if (foreign.Count > 0) return e.Hp <= 2 ? 1000 : 4.5;
                return -1.5;
            case "WCG-330":
                var t330 = e.Field.Where(x => x.IsUnit && (x.Card.PP ?? 0) <= 1200).ToList();
                if (t330.Count == 0) return -5;
                return t330.Any(x => x.IsTapped) && SacrificeOutlet(p) ? 3 : 1.2;
            case "WCG-331":
                var t331 = e.Field.Where(x => x.IsUnit && (x.Card.PP ?? 0) <= 1800).ToList();
                return t331.Count == 0 ? -5 : t331.Max(Steal) + 0.5;
            case "WCG-335":
                var all = e.Field.Where(x => x.IsUnit).ToList();
                return all.Count == 0 || p.Occupied >= 5 ? -5 : all.Max(x => BodyValue(x) + x.CurrentDP + (x.IsTaunt ? 3 : 0)) + 2;
            case "WCG-336":
                return foreign.Count > 0 ? 3.5 + foreign.Max(x => x.Card.TotalCost) * .3 : -1;
        }
        return GravePlayValue(p, c);
    }
    private MonsterInstance? SoulTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) => pending.Title switch
    {
        SoulSacrificeTitle => legal.OrderByDescending(m => (m.Turned ? 10 : 0) - BodyValue(m)).FirstOrDefault(),
        SoulReturnTitle => legal.OrderByDescending(m => m.Turned ? 0 : m.Card.TotalCost).FirstOrDefault(),
        _ => GraveTarget(p, pending, legal)
    };
}
