using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「焚能爆破」（狂怒＋深淵，WCG-310–318，2026-10-23）：把自己能量區的牌送入墓地，換便宜的直傷與爆發。
// Burning may take upright (unused) or tapped energy unless the card says 「橫置的」. A burned card goes to the graveyard
// face up (public). Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string BurnTitle = "選擇送入墓地的能量牌";
    internal const string BurnOptionalTitle = "可選擇送入墓地的能量牌";
    internal const string WorldFlameTitle = "焚世魔焰：這張能量換什麼？";
    internal const string AshReviveTitle = "選擇從墓地召喚的費用 3 以下怪物";
    internal const string BloodAshTitle = "選擇背面放入能量區的墓地牌";
    private string? burnSourceId; // which card is asking (AI only); never set while the engine is quiescent

    private void BurnOne(PlayerState p, CardInstance c)
    {
        if (!p.EnergyZone.Remove(c)) return;
        c.IsTapped = false; c.TemporaryEnergy = false; p.Graveyard.Add(c); p.BurnedEnergy[c.InstanceId] = TurnNumber;
        Present("discard", p, c.InstanceId, card: c.Card, label: "能量送入墓地");
        Log($"【{p.Name}】將能量區的【{c.Card.Name}】送入墓地。", "action");
        foreach (var priest in p.Field.Where(x => Active(x, "WCG-312"))) AddTurnBonus(priest, 300, priest.Card);
        RefreshBoard();
    }
    // Burn up to max cards one at a time (each optional when optional), then after(count).
    private void BurnEnergy(PlayerState p, string sourceId, int max, bool optional, Func<CardInstance, bool> which, Action<int> after, int done = 0)
    {
        var list = p.EnergyZone.Where(which).ToList();
        if (done >= max || list.Count == 0) { burnSourceId = null; after(done); return; }
        var options = list.Select(c => new ChoiceOption { Id = c.InstanceId.ToString(), Title = c.Card.Name, PreviewCard = c.Card, Subtitle = c.IsTapped ? "橫置（已使用）" : "直立（未使用）" }).ToList();
        if (optional) options.Add(new() { Id = "SKIP", Title = done == 0 ? "不送入墓地" : "停止" });
        burnSourceId = sourceId;
        Ask(p, optional ? BurnOptionalTitle : BurnTitle, options, o =>
        {
            var c = list.FirstOrDefault(x => x.InstanceId.ToString() == o.Id);
            if (c == null) { burnSourceId = null; after(done); return; }
            BurnOne(p, c);
            BurnEnergy(p, sourceId, max, optional, which, after, done + 1);
        });
    }
    private bool BurnedThisTurn(PlayerState p, CardInstance c) => p.BurnedEnergy.TryGetValue(c.InstanceId, out var turn) && turn == TurnNumber;

    private bool EmberDeploy(PlayerState p, MonsterInstance m)
    {
        var e = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "WCG-310": BurnEnergy(p, m.Card.Id, 1, true, _ => true, n => { if (n > 0) Damage(e, 1, new(p, m.Card, m, "deploy")); }); break;
            case "WCG-313": BurnEnergy(p, m.Card.Id, 1, true, _ => true, n => { if (n > 0 && Alive(m)) { AddTurnBonus(m, 500, m.Card); RefreshBoard(); } }); break;
            case "WCG-318": WorldFlame(p, m, 0); break;
            default: return LaneDeploy(p, m);
        }
        return true;
    }
    // 焚世魔焰: up to 3 times — burn 1 energy, then destroy 1 enemy monster with printed PP 900 or less, or deal 1 damage.
    private void WorldFlame(PlayerState p, MonsterInstance m, int done)
    {
        if (done >= 3 || p.EnergyZone.Count == 0) return;
        BurnEnergy(p, m.Card.Id, 1, true, _ => true, n =>
        {
            if (n == 0) return;
            var e = GetOpponent(p); var small = e.Field.Where(x => x.IsUnit && ThresholdPP(x) <= 900).ToList();
            var options = new List<ChoiceOption> { new() { Id = "FACE", Title = "對敵方玩家造成 1 點傷害" } };
            if (small.Count > 0) options.Insert(0, new() { Id = "KILL", Title = "消滅 1 隻印刷 PP 900 以下的敵方怪物" });
            Ask(p, WorldFlameTitle, options, o =>
            {
                if (o.Id == "KILL") Resolve(() => PickMonster(p, "選擇印刷 PP 900 以下的敵方怪物", GetOpponent(p).Field.Where(x => x.IsUnit && ThresholdPP(x) <= 900), x => KillBatch([x], m.Card.Name)), () => WorldFlame(p, m, done + 1));
                else Resolve(() => Damage(GetOpponent(p), 1, new(p, m.Card, m, "deploy")), () => WorldFlame(p, m, done + 1));
            });
        });
    }
    private bool EmberSpell(PlayerState p, CardInstance source)
    {
        var e = GetOpponent(p);
        switch (source.Card.Id)
        {
            case "WCG-311": BurnEnergy(p, source.Card.Id, 2, false, _ => true, n => { if (n >= 2) Damage(e, 2, new(p, source.Card, null, "spell")); }); break;
            case "WCG-315": BurnEnergy(p, source.Card.Id, 3, true, c => c.IsTapped, n => { if (n > 0) Damage(e, n, new(p, source.Card, null, "spell")); }); break;
            case "WCG-314":
                if (p.Occupied >= 5) break;
                CardChoice(p, AshReviveTitle, p.Graveyard.Where(c => c.Card.IsMonster && c.Card.TotalCost <= 3), c =>
                {
                    var upright = BurnedThisTurn(p, c);
                    if (p.Occupied < 5) ChooseSlot(p, c.Card, slot =>
                    {
                        if (!p.Graveyard.Remove(c)) return; plannedSlots[c.InstanceId] = slot; Summon(p, c, false);
                        if (upright && p.Field.FirstOrDefault(x => x.InstanceId == c.InstanceId) is { } m) { m.IsTapped = false; RefreshBoard(); }
                    });
                });
                break;
            case "WCG-317":
                Resolve(() => CardChoice(p, BloodAshTitle, p.Graveyard.Where(c => c != source), c => { if (p.Graveyard.Remove(c)) PutIntoEnergy(p, c); }), () => DrawMany(p, 1));
                break;
            default: return false;
        }
        return true;
    }
    private string EmberPlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-311" when p.EnergyZone.Count < 2 => "能量區不足 2 張牌可以送入墓地。",
        "WCG-314" when p.Occupied >= 5 => "場上沒有空位。",
        _ => HoofPlayProblem(p, c)
    };
    private bool EmberDeath(PlayerState p, MonsterInstance m, CardInstance grave)
    {
        if (m.Card.Id != "WCG-316") return false;
        if (p.EnergyZone.Count > 0) { Present("trigger", p, m.InstanceId, card: m.Card, label: "陣亡能力"); BurnEnergy(p, m.Card.Id, 1, false, _ => true, n => { if (n > 0) Damage(GetOpponent(p), 1, new(p, m.Card, m, "death")); }); }
        return true;
    }

    // ── 天梯電腦 ──
    private double EnergyLoss(PlayerState p, int n) => n * (p.TotalEnergy <= 4 ? 3 : p.TotalEnergy <= 6 ? 1.8 : 1);
    private double FaceValue(PlayerState e, int n) => e.Hp <= n ? 1000 : n * (e.Hp <= 3 ? 5 : 3.5);
    private double? EmberPlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        switch (c.Card.Id)
        {
            case "WCG-311": return FaceValue(e, 2) - EnergyLoss(p, 2) - 1;
            case "WCG-315": var n = Math.Min(3, p.EnergyZone.Count(x => x.IsTapped) + ActualCost(p, c.Card)); return FaceValue(e, n) - EnergyLoss(p, n) - 2;
            case "WCG-314": return p.Graveyard.Where(x => x.Card.IsMonster && x.Card.TotalCost <= 3).Select(x => CardValue(x.Card)).DefaultIfEmpty(-6).Max() - 2;
            case "WCG-317": return (p.Graveyard.Count > 0 ? 1.5 : 0) + (p.Deck.Count > 3 ? 2 : -8) - 1;
        }
        return HoofPlayValue(p, c);
    }
    // Should the AI burn one more energy for this source? Burn tapped (already used) energy first.
    private bool BurnWorth(PlayerState p)
    {
        var e = GetOpponent(p);
        return burnSourceId switch
        {
            "WCG-310" or "WCG-318" => e.Hp <= 3 || p.TotalEnergy >= 6,
            "WCG-313" => p.TotalEnergy >= 6 || e.Hp <= 2,
            "WCG-315" => true,
            _ => true,
        };
    }
    private ChoiceOption? EmberChoice(PlayerState p, PendingChoice choice)
    {
        var e = GetOpponent(p);
        switch (choice.Title)
        {
            case BurnTitle:
            case BurnOptionalTitle:
                var skip = choice.Options.FirstOrDefault(o => o.Id == "SKIP");
                if (skip != null && !BurnWorth(p)) return skip;
                var wantRevive = p.Hand.Any(x => x.Card.Id == "WCG-314");
                double Score(ChoiceOption o) => p.EnergyZone.FirstOrDefault(c => c.InstanceId.ToString() == o.Id) is { } c
                    ? (c.IsTapped ? 10 : 0) + (wantRevive && c.Card.IsMonster && c.Card.TotalCost <= 3 ? CardValue(c.Card) : -HoldValue(p, c) * .2) : -99;
                return choice.Options.Where(o => o.Id != "SKIP").OrderByDescending(Score).First();
            case WorldFlameTitle:
                var kill = choice.Options.FirstOrDefault(o => o.Id == "KILL");
                var best = e.Field.Where(x => x.IsUnit && ThresholdPP(x) <= 900).Select(BodyValue).DefaultIfEmpty(0).Max();
                return kill != null && best >= 4 && e.Hp > 1 ? kill : choice.Options.First(o => o.Id == "FACE");
            case AshReviveTitle:
                return choice.Options.Where(o => o.Id != "SKIP").OrderByDescending(o => p.Graveyard.FirstOrDefault(c => c.InstanceId.ToString() == o.Id) is { } c ? CardValue(c.Card) + (BurnedThisTurn(p, c) ? 3 : 0) : -99).First();
            case BloodAshTitle:
                return choice.Options.Where(o => o.Id != "SKIP").OrderByDescending(o => p.Graveyard.FirstOrDefault(c => c.InstanceId.ToString() == o.Id) is { } c ? (c.Card.IsMonster ? c.Card.TotalCost : 0) : -99).First();
        }
        return null;
    }
}
