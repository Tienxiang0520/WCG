using WcgWeb.Models;
namespace WcgWeb.Services;

// 規則簡化（PR A）：能量只付費用、聖盾每回合重置、貫穿無 PP 差條件、PP 門檻 3 級、反擊每次傷害最多 1 張。
// The catalog wording selects these rules, so saved ranked matches on older catalogs replay with their original engine.
public partial class GameEngine
{
    public bool V2Rules => CardBalanceHistory.V2Catalog(_cardDb);
    public bool SimplifiedRules => V2Rules || CardBalanceHistory.SimplifiedCatalog(_cardDb);
    private int ScalePP(int pp) => V2Rules ? pp / 100 : pp;
    // Printed "PP n 以下" thresholds collapse to 600 / 1100 (or 900 / 1200 after the 2026-10-22 curve update); "PP n 以上" thresholds collapse to 1800. v2.0 uses 10 / 18.
    private int Low(int printed) => V2Rules ? 10 : !SimplifiedRules ? printed : CardBalanceHistory.CardFixesCatalog(_cardDb) ? (printed <= 800 ? 900 : 1200) : (printed <= 800 ? 600 : 1100);
    private int High(int printed) => V2Rules ? 18 : !SimplifiedRules ? printed : 1800;
    private int SkyfireLimit() => V2Rules ? 18 : CardBalanceHistory.CardFixesCatalog(_cardDb) ? 2300 : 1500;
    // PP thresholds ("PP n 以下／以上", 059's taunt limit, 195's bonus) check the target's printed PP from 2026-10-22 to pre-v2; v2.0 always checks current PP.
    public bool PrintedPpThresholds => !V2Rules && CardBalanceHistory.CardFixesCatalog(_cardDb);
    // From the same catalog, "PP 最低" picks (007/016/107/117/120, ties chosen by the controller) and 248's "PP 比它低" compare printed PP too.
    private int ThresholdPP(MonsterInstance m) => PrintedPpThresholds ? m.Card.PP ?? 0 : m.CurrentPP;
    public const string OathGuardTitle = "護誓守衛：要橫置它使傷害 -1 嗎？";
    // 聖盾: the first time each turn this monster would be destroyed in combat, it survives instead. No energy is paid.
    // v2.0 格擋: upright attacked monster that would be destroyed in combat taps and survives instead (no hand-card stuffing).
    public bool ShieldSourceRules => !V2Rules && SimplifiedRules && (DeckCardRules || CardBalanceHistory.ShieldCounterCatalog(_cardDb));
    private bool ShieldReady(MonsterInstance m) => V2Rules ? m.HasShield && !m.IsTapped : SimplifiedRules ? (ShieldSourceRules ? m.HasShield : !m.ShieldSpent) : Owner(m).AvailableEnergy > 0;
    // 304 能量哨兵 can keep itself alive with its energy when it has no 聖盾.
    private bool EnergyGuard(MonsterInstance m) => ShieldSourceRules && !m.HasShield && Active(m, "WCG-304") && Owner(m).EnergyZone.Count >= 5;
    private bool FixedAttackBonus(MonsterInstance a) => SimplifiedRules && !V2Rules && !CardBalanceHistory.CardFixesCatalog(_cardDb) && a.IsUnit && !a.IsSilenced && a.Card.Id == "WCG-201";
    // Fixed bonuses that only exist during an attack's combat (203 +500; from 2026-10-23 also 274 +300 for itself).
    private int CombatPower(MonsterInstance m, MonsterInstance other, bool attacking) => !SimplifiedRules || !m.IsUnit ? 0 : (m.IsSilenced || V2Rules ? 0 : m.Card.Id switch { "WCG-203" => 500, "WCG-274" => 300, _ => 0 }) + ArchetypeCombatPower(m, other, attacking);
    private void ApplyCombatPower(MonsterInstance a, MonsterInstance d)
    { var x = CombatPower(a, d, true); var y = CombatPower(d, a, false); if (x != 0) a.CombatPowerBonus = x; if (y != 0) d.CombatPowerBonus = y; }
    // 143／165／168 are attached; the attachment is the visible reminder and goes to the graveyard after the monster's next combat.
    internal static bool CombatAttachment(string id) => id is "WCG-143" or "WCG-165" or "WCG-168" or "WCG-271" or "WCG-277" or "WCG-284";
    private int AttachmentPower(string id) => id switch { "WCG-135" or "WCG-143" or "WCG-165" => ScalePP(500), "WCG-141" => ScalePP(700), "WCG-168" => ScalePP(-500), "WCG-271" => ScalePP(400), "WCG-277" => ScalePP(500), "WCG-284" => ScalePP(-700), "WCG-288" => ScalePP(-500), _ => 0 };
    private void SpendCombatAttachments(MonsterInstance m)
    {
        if (!Alive(m)) return;
        var spent = m.Attachments.Where(a => CombatAttachment(a.Card.Card.Id)).ToArray();
        if (spent.Length == 0) return;
        foreach (var a in spent) { m.Attachments.Remove(a); PlayerById(a.OwnerId).Graveyard.Add(a.Card); }
        RefreshBoard();
        Present("status", Owner(m), m.InstanceId, card: m.Card, label: "交戰後附著卡送入墓地");
    }
    private void ApplyShield(MonsterInstance m, bool hit, Action<bool> finish, bool lethal = true)
    {
        if (V2Rules)
        {
            if (!hit || !lethal || !Alive(m) || !m.HasShield || m.IsTapped) { finish(false); return; }
            m.IsTapped = true; RefreshBoard();
            Present("status", Owner(m), m.InstanceId, card: m.Card, label: "格擋橫置保命");
            Log($"【{m.Card.Name}】處於直立且具有格擋，改為橫置並存活。", "combat");
            finish(true);
            return;
        }
        if (ShieldSourceRules) { if (!hit || !Alive(m)) finish(false); else SpendShield(m, lethal, finish); return; }
        if (!hit || !Alive(m) || !m.HasShield || m.ShieldSpent) { finish(false); return; }
        m.ShieldSpent = true;
        Present("status", Owner(m), m.InstanceId, card: m.Card, label: "聖盾抵擋消滅");
        Log($"【{m.Card.Name}】的聖盾抵擋了這次消滅（本回合已用）。", "combat");
        finish(true);
    }
    private MonsterInstance[] OathGuards(PlayerState p) => p.Field.Where(m => m.IsUnit && !m.IsSilenced && !m.IsTapped && m.Card.Id == "WCG-200").ToArray();
    private void OathGuardDamage(PlayerState p, int amount, DamageSource? source)
    {
        var guards = OathGuards(p);
        var maximum = Math.Min(amount, guards.Length);
        if (maximum == 0) { ApplyPlayerDamage(p, amount, source); return; }
        var options = Enumerable.Range(1, maximum).Select(n => new ChoiceOption { Id = "GUARD:" + n, Title = $"橫置 {n} 隻護誓守衛，傷害 -{n}" })
            .Append(new() { Id = "GUARD:0", Title = "不橫置，接受傷害" });
        Ask(p, OathGuardTitle, options, option =>
        {
            var used = OathGuards(p).Take(Math.Min(int.Parse(option.Id.AsSpan(6)), amount)).ToArray();
            foreach (var g in used) { g.IsTapped = true; Present("status", p, g.InstanceId, card: g.Card, label: "護誓守衛減少傷害"); }
            if (used.Length > 0) Log($"【{p.Name}】橫置 {used.Length} 隻護誓守衛，傷害 -{used.Length}。", "action");
            ApplyPlayerDamage(p, amount - used.Length, source);
        }, sourceInstanceId: guards[0].InstanceId);
        CurrentPendingChoice!.SourceCard = guards[0].Card;
        CurrentPendingChoice.Description = $"將受到 {amount} 點傷害；每橫置 1 隻直立的護誓守衛，傷害 -1。";
    }
}
