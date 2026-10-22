using WcgWeb.Models;
namespace WcgWeb.Services;

// 規則簡化（PR A）：能量只付費用、聖盾每回合重置、貫穿無 PP 差條件、PP 門檻 3 級、反擊每次傷害最多 1 張。
// The catalog wording selects these rules, so saved ranked matches on older catalogs replay with their original engine.
public partial class GameEngine
{
    public bool SimplifiedRules => CardBalanceHistory.SimplifiedCatalog(_cardDb);
    // Printed "PP n 以下" thresholds collapse to 600 / 1100 (or 900 / 1200 after the 2026-10-22 curve update); "PP n 以上" thresholds collapse to 1800.
    private int Low(int printed) => !SimplifiedRules ? printed : CardBalanceHistory.CardFixesCatalog(_cardDb) ? (printed <= 800 ? 900 : 1200) : (printed <= 800 ? 600 : 1100);
    private int High(int printed) => !SimplifiedRules ? printed : 1800;
    private int SkyfireLimit() => CardBalanceHistory.CardFixesCatalog(_cardDb) ? 2300 : 1500;
    public const string OathGuardTitle = "護誓守衛：要橫置它使傷害 -1 嗎？";
    // 聖盾: the first time each turn this monster would be destroyed in combat, it survives instead. No energy is paid.
    // 聖盾指示物 (2026-10-17): a monster that gains 聖盾 (on entry or later) gets one counter; the first combat destruction
    // removes it instead. No refresh. Losing 聖盾 (silence, 147's arrow, condition ends) removes the counter. Older catalogs refresh each turn.
    public bool ShieldCounterRules => CardBalanceHistory.ShieldCounterCatalog(_cardDb);
    private bool ShieldReady(MonsterInstance m) => SimplifiedRules ? (ShieldCounterRules ? m.ShieldCounters > 0 : !m.ShieldSpent) : Owner(m).AvailableEnergy > 0;
    private bool FixedAttackBonus(MonsterInstance a) => SimplifiedRules && !CardBalanceHistory.CardFixesCatalog(_cardDb) && a.IsUnit && !a.IsSilenced && a.Card.Id == "WCG-201";
    private bool FixedCombatBonus(MonsterInstance m) => SimplifiedRules && m.IsUnit && !m.IsSilenced && m.Card.Id == "WCG-203";
    // 143／165／168 are attached; the attachment is the visible reminder and goes to the graveyard after the monster's next combat.
    internal static bool CombatAttachment(string id) => id is "WCG-143" or "WCG-165" or "WCG-168";
    internal static int AttachmentPower(string id) => id switch { "WCG-135" or "WCG-143" or "WCG-165" => 500, "WCG-141" => 700, "WCG-168" => -500, _ => 0 };
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
        if (!hit || !Alive(m) || !m.HasShield || m.ShieldSpent) { finish(false); return; }
        if (ShieldCounterRules) { m.ShieldCounters = Math.Max(0, m.ShieldCounters - 1); m.ShieldQualified = m.ShieldCounters > 0; m.ShieldSpent = m.ShieldCounters == 0; }
        else m.ShieldSpent = true;
        Present("status", Owner(m), m.InstanceId, card: m.Card, label: lethal ? "聖盾抵擋消滅" : "受到攻擊，消耗聖盾");
        if (ShieldCounterRules) Log(lethal ? $"【{m.Card.Name}】移除 1 個聖盾指示物，抵擋了這次消滅。" : $"【{m.Card.Name}】受到攻擊，移除 1 個聖盾指示物。", "combat");
        else Log($"【{m.Card.Name}】的聖盾抵擋了這次消滅（本回合已用）。", "combat");
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
