using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「伏能喚醒」（理智＋秩序，WCG-301–309，2026-10-23）：把怪物蓋進能量區，之後不付費從能量區召喚。
// Energy-zone rules: a player always sees their own energy; the opponent never learns a face-down energy card
// (no card is attached to the presentation event, the log does not name it, and AI simulations hide it).
// A card summoned from the energy zone is face up and public. Summoning from energy is not a paid summon: 進場 does not trigger.
// Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string HideMonsterTitle = "可選擇 1 張手牌怪物背面放入能量區";
    internal const string HideCardTitle = "選擇 1 張手牌背面放入能量區";
    internal const string EnergySummonTitle = "選擇從能量區召喚的怪物";
    internal const string EnergySummonMoreTitle = "可再選擇 1 隻從能量區召喚的怪物";
    internal const string SwapHandTitle = "可選擇 1 張手牌放入能量區（交換）";
    internal const string SwapEnergyTitle = "選擇換回手牌的能量牌";

    private static IEnumerable<CardInstance> RealEnergy(PlayerState p) => p.EnergyZone.Where(c => !c.TemporaryEnergy);
    // Face down and tapped (or keeping a given state) into the energy zone; does not count as the turn's energy fill.
    private void PutIntoEnergy(PlayerState p, CardInstance c, bool tapped = true)
    {
        c.IsTapped = tapped; c.TemporaryEnergy = false; p.EnergyZone.Add(c);
        Present("energy", p, c.InstanceId, label: "背面放入能量區");
        Log($"【{p.Name}】將 1 張牌背面放入能量區。", "action");
    }
    private void HideFromHand(PlayerState p, string title, Func<CardInstance, bool> which, bool optional)
    {
        CardChoice(p, title, p.Hand.Where(which), c => { if (p.Hand.Remove(c)) PutIntoEnergy(p, c); }, optional);
    }
    // 不付費從能量區召喚（不觸發進場）。309 伏兵號令 this turn: upright, can attack, +300 until end of turn.
    private void SummonFromEnergy(PlayerState p, CardInstance c, bool upright, Action? after = null)
    {
        if (p.Occupied >= 5 || !p.EnergyZone.Contains(c)) { after?.Invoke(); return; }
        ChooseSlot(p, c.Card, slot =>
        {
            if (!p.EnergyZone.Remove(c) || p.Occupied >= 5) { after?.Invoke(); return; }
            c.IsTapped = false; c.TemporaryEnergy = false;
            plannedSlots[c.InstanceId] = slot; Summon(p, c, false);
            var m = p.Field.FirstOrDefault(x => x.InstanceId == c.InstanceId);
            if (m != null)
            {
                m.FromEnergy = true;
                var ambush = p.EnergyAmbushTurn == TurnNumber;
                m.IsTapped = !(upright || ambush || m.HasCharge);
                if (ambush) { m.HasSummoningSickness = false; AddTurnBonus(m, 300, _cardDb.GetCard("WCG-309")!); }
                RefreshBoard();
                Log($"【{p.Name}】從能量區召喚【{c.Card.Name}】（不觸發進場）。", "action");
            }
            after?.Invoke();
        });
    }
    private void OfferEnergySummon(PlayerState p, string title, Func<CardInstance, bool> which, bool upright, bool optional, Action? after = null)
    {
        var list = RealEnergy(p).Where(c => c.Card.IsMonster && which(c)).ToList();
        if (list.Count == 0 || p.Occupied >= 5) { after?.Invoke(); return; }
        var options = list.Select(c => new ChoiceOption { Id = c.InstanceId.ToString(), Title = c.Card.Name, PreviewCard = c.Card, Subtitle = c.IsTapped ? "橫置" : "直立" }).ToList();
        if (optional) options.Add(new() { Id = "SKIP", Title = "不召喚" });
        Ask(p, title, options, o =>
        {
            var c = list.FirstOrDefault(x => x.InstanceId.ToString() == o.Id);
            if (c == null) { after?.Invoke(); return; }
            SummonFromEnergy(p, c, upright, after);
        });
    }
    private IEnumerable<(CardDefinition Source, int Amount)> VaultPowerSources(PlayerState p, MonsterInstance m)
    {
        if (m.IsUnit && !m.IsSilenced && m.FromEnergy && m.Card.Id == "WCG-306") yield return (m.Card, 300);
    }

    private bool VaultDeploy(PlayerState p, MonsterInstance m)
    {
        switch (m.Card.Id)
        {
            case "WCG-301": HideFromHand(p, HideMonsterTitle, c => c.Card.IsMonster, true); break;
            case "WCG-304": if (p.EnergyZone.Count >= 5) GainShieldCounter(m, m.Card); break;
            case "WCG-308":
                var energy = RealEnergy(p).ToList();
                if (energy.Count == 0) break;
                CardChoice(p, SwapHandTitle, p.Hand, h => CardChoice(p, SwapEnergyTitle, RealEnergy(p).ToList(), en =>
                {
                    if (!p.Hand.Contains(h) || !p.EnergyZone.Contains(en)) return;
                    var tapped = en.IsTapped; p.EnergyZone.Remove(en); en.IsTapped = false; p.Hand.Add(en); p.Hand.Remove(h);
                    PutIntoEnergy(p, h, tapped);
                    Present("draw", p, en.InstanceId, label: "能量牌回到手牌");
                }), true);
                break;
            default: return EmberDeploy(p, m);
        }
        return true;
    }
    private bool VaultSpell(PlayerState p, CardInstance source)
    {
        switch (source.Card.Id)
        {
            case "WCG-302": Resolve(() => HideFromHand(p, HideCardTitle, _ => true, false), () => DrawMany(p, 1)); break;
            case "WCG-303": OfferEnergySummon(p, EnergySummonTitle, c => c.Card.TotalCost <= 4, true, false); break;
            case "WCG-307": OfferEnergySummon(p, EnergySummonTitle, _ => true, false, false, () => OfferEnergySummon(p, EnergySummonMoreTitle, _ => true, false, true)); break;
            case "WCG-309": p.EnergyAmbushTurn = TurnNumber; Log($"【{p.Name}】本回合從能量區召喚的怪物直立進場、可以攻擊，PP +300。", "action"); break;
            default: return EmberSpell(p, source);
        }
        return true;
    }
    private string VaultPlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-303" when !RealEnergy(p).Any(x => x.Card.IsMonster && x.Card.TotalCost <= 4) => "能量區沒有費用 4 以下的怪物牌。",
        "WCG-307" when !RealEnergy(p).Any(x => x.Card.IsMonster) => "能量區沒有怪物牌。",
        "WCG-303" or "WCG-307" when p.Occupied >= 5 => "場上沒有空位。",
        "WCG-302" when p.Hand.Count < 2 => "沒有其他手牌可以放入能量區。",
        _ => EmberPlayProblem(p, c)
    };
    private bool VaultDeath(PlayerState p, MonsterInstance m, CardInstance grave)
    {
        if (m.Card.Id != "WCG-305") return EmberDeath(p, m, grave);
        if (p.Graveyard.Remove(grave)) { Present("trigger", p, m.InstanceId, card: m.Card, label: "陣亡能力"); PutIntoEnergy(p, grave); }
        return true;
    }

    // ── 天梯電腦 ──
    private double EnergyMonsterValue(CardInstance c) => CardValue(c.Card);
    private double? VaultPlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        double Best(Func<CardInstance, bool> which, int n) => RealEnergy(p).Where(x => x.Card.IsMonster && which(x)).Select(EnergyMonsterValue).OrderByDescending(v => v).Take(n).Sum();
        var room = 5 - p.Occupied;
        switch (c.Card.Id)
        {
            case "WCG-303": return room <= 0 ? -5 : Best(x => x.Card.TotalCost <= 4, 1) - 3;
            case "WCG-307": return room <= 0 ? -5 : Best(_ => true, Math.Min(2, room)) - 4.5;
            case "WCG-309":
                var follow = p.Hand.Any(x => x.Card.Id is "WCG-303" or "WCG-307" && ActualCost(p, x.Card) + 2 <= p.AvailableEnergy && VaultPlayProblem(p, x.Card) == "");
                return follow ? 3 : -6;
            case "WCG-302": return p.Hand.Any(x => x != c && x.Card.IsMonster && x.Card.TotalCost >= 5) ? 2.5 : .5;
            case "WCG-301": return p.Hand.Any(x => x.Card.IsMonster && x.Card.TotalCost >= 5) ? 1.5 : 0;
            case "WCG-308": return p.Hand.Any(x => x.Card.IsMonster && x.Card.TotalCost >= 5) || RealEnergy(p).Any(x => !x.Card.IsMonster) ? 1 : 0;
        }
        return EmberPlayValue(p, c);
    }
    // Which hand card to hide: the biggest monster we can't cast soon (it comes back with 303/307), else the least useful card.
    private double HideScore(PlayerState p, CardInstance c) => (c.Card.IsMonster ? c.Card.TotalCost * 2 + (c.Card.TotalCost > p.TotalEnergy + 1 ? 4 : 0) : 0) - HoldValue(p, c) * .5;
    private ChoiceOption? VaultChoice(PlayerState p, PendingChoice choice)
    {
        CardInstance? Card(ChoiceOption o, IEnumerable<CardInstance> zone) => zone.FirstOrDefault(c => c.InstanceId.ToString() == o.Id);
        var real = choice.Options.Where(o => o.Id != "SKIP").ToList();
        switch (choice.Title)
        {
            case HideMonsterTitle:
            case HideCardTitle:
            case SwapHandTitle:
                var best = real.OrderByDescending(o => Card(o, p.Hand) is { } c ? HideScore(p, c) : -99).FirstOrDefault();
                var skip = choice.Options.FirstOrDefault(o => o.Id == "SKIP");
                if (skip != null && (best == null || Card(best, p.Hand) is not { } bc || !bc.Card.IsMonster || bc.Card.TotalCost < 4)) return skip;
                return best ?? choice.Options[0];
            case SwapEnergyTitle:
                return real.OrderByDescending(o => Card(o, p.EnergyZone) is { } c ? HoldValue(p, c) - (c.Card.IsMonster && c.Card.TotalCost >= 5 ? 4 : 0) : -99).First();
            case EnergySummonTitle:
            case EnergySummonMoreTitle:
                return real.OrderByDescending(o => Card(o, p.EnergyZone) is { } c ? EnergyMonsterValue(c) : -99).FirstOrDefault() ?? choice.Options[0];
        }
        return null;
    }
}
