using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「聖泉滿溢」（秩序＋中立，WCG-400–408，2026-10-25）：每一點回血都從牌庫底墊 1 張聖泉在井底下，最後倒到對手臉上。
// 聖泉 cards sit under 403 聖泉之井 (a structure), one per point healed, including healing beyond the 7-life maximum.
// Silence never touches them; they go back to the deck bottom when the well leaves play. "Healed this turn" (406) also counts the overflow.
// Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string SpringBaptismTitle = "洗禮之潮：選擇敵方怪物（本回合 PP 降低）";

    private int HealedThisTurn(PlayerState p) => p.SpringHealTurn == TurnNumber ? p.SpringHealed : 0;
    // Called by Heal with the full amount (overflow included).
    private void SpringHealed(PlayerState p, int amount)
    {
        if (amount <= 0) return;
        if (p.SpringHealTurn != TurnNumber) { p.SpringHealTurn = TurnNumber; p.SpringHealed = 0; }
        p.SpringHealed += amount;
        foreach (var well in p.Structures.Where(x => !x.IsSet && x.Card.Id == "WCG-403"))
        {
            PadUnder(p, well, UnderKind.HolySpring, amount, well.Card);
        }
        foreach (var acolyte in p.Field.Where(x => Active(x, "WCG-401")).ToArray())
            foreach (var x in PointedUnits(acolyte)) AddTurnBonus(x, ScalePP(300), acolyte.Card);
        foreach (var knight in p.Field.Where(x => Active(x, "WCG-405")).ToArray())
        {
            if (knight.SpringDpTurn != TurnNumber) { knight.SpringDpTurn = TurnNumber; knight.SpringDpCount = 0; }
            if (knight.SpringDpCount >= 2) continue;
            knight.SpringDpCount++; AddDpCards(p, knight, knight.Card);
        }
        RefreshBoard();
    }
    private static int SpringCards(PlayerState p) => p.Structures.Where(x => !x.IsSet && x.Card.Id == "WCG-403").Sum(x => x.SpringCards);
    // Monsters a spell may pick (禁魔石像 152 and other spell-immune monsters are skipped).
    private List<MonsterInstance> SpellPickable(PlayerState p, IEnumerable<MonsterInstance> source) =>
        Targetable(p, source).Where(m => (m.IsSilenced || (V2Rules ? !m.Card.HasSpellward : m.Card.Id != "WCG-152")) && (V2Rules || !Hermit(p, m))).ToList();
    // Monsters a spell may choose while it resolves (禁魔石像 152 / 魔免; 封時守衛 413 while it has Countdown counters).
    // Area effects that don't choose still hit them.
    private List<MonsterInstance> SpellResolvePickable(PlayerState p, IEnumerable<MonsterInstance> source) =>
        Targetable(p, source).Where(m => (m.IsSilenced || (V2Rules ? !m.Card.HasSpellward : m.Card.Id != "WCG-152")) && !StarWarded(m)).ToList();
    // 406／420: -X PP until end of turn; destroyed if that brings it to 0.
    private void ShrinkUntilEndOfTurn(MonsterInstance x, int amount, CardDefinition source)
    {
        if (!Alive(x) || amount <= 0) return;
        AddTurnBonus(x, -amount, source); RefreshBoard();
        Log($"【{x.Card.Name}】本回合 PP -{amount}（PP {x.CurrentPP}）。", "action");
        if (x.CurrentPP <= 0) KillBatch([x], source.Name);
    }

    private bool SpringDeploy(PlayerState p, MonsterInstance m)
    {
        switch (m.Card.Id)
        {
            case "WCG-400": Heal(p, 1); break;
            case "WCG-407": Heal(p, 3); break;
            default: return StarDeploy(p, m);
        }
        return true;
    }
    private void SpringEndTurn(PlayerState p, List<Action> effects)
    {
        foreach (var m in p.Field.Where(x => Active(x, "WCG-404")).ToArray())
            effects.Add(() => { if (Alive(m) && Active(m, "WCG-404")) { Present("trigger", p, m.InstanceId, card: m.Card, label: m.Card.Name); Heal(p, 1); } });
    }
    private void SpringStartTurn(PlayerState p)
    {
        foreach (var m in p.Field.Where(x => Active(x, "WCG-407")).ToArray())
            _effects.AddLast(() =>
            {
                if (!Alive(m) || !Active(m, "WCG-407") || p.Hp != PlayerState.MaxHp || IsOver) return;
                Present("trigger", p, m.InstanceId, card: m.Card, label: m.Card.Name);
                Damage(GetOpponent(p), 1, new(p, m.Card, m, "passive"));
            });
    }
    private bool SpringSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        var e = GetOpponent(p);
        switch (source.Card.Id)
        {
            case "WCG-402":
                var full = p.Hp >= PlayerState.MaxHp;
                Heal(p, 2); if (full) DrawMany(p, 1);
                break;
            case "WCG-406":
                Heal(p, 2);
                var amount = 300 * HealedThisTurn(p);
                PickMonster(p, SpringBaptismTitle, SpellPickable(p, e.Field.Where(x => x.IsUnit)), x => ShrinkUntilEndOfTurn(x, amount, source.Card));
                break;
            case "WCG-408":
                Heal(p, 2);
                var n = 0;
                foreach (var well in p.Structures.Where(x => !x.IsSet && x.Card.Id == "WCG-403").ToArray()) n += ReturnUnder(well, UnderKind.HolySpring, well.SpringCards);
                Log($"【{p.Name}】把聖泉之井底下 {n} 張聖泉放回牌庫底。", "action");
                if (n / 2 > 0) Damage(e, n / 2, new(p, source.Card, null, "spell"));
                break;
            default: return StarSpell(p, source, t);
        }
        RefreshBoard();
        return true;
    }
    // 403 聖泉之井: tap and put 2 聖泉 cards from under it back on the deck bottom to draw 1.
    private static bool SpringCanActivate(PlayerState p, MonsterInstance s) => s.Card.Id == "WCG-403" && s.SpringCards >= 2 || FateCanActivate(p, s);
    private void SpringActivate(PlayerState p, MonsterInstance s)
    {
        if (s.Card.Id != "WCG-403") { FateActivate(p, s); return; }
        if (s.SpringCards < 2) return;
        s.IsTapped = true; ReturnUnder(s, UnderKind.HolySpring, 2); DrawMany(p, 1);
    }

    // ── 天梯電腦 ──
    private double? SpringPlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        switch (c.Card.Id)
        {
            case "WCG-402": return p.Hp >= PlayerState.MaxHp ? 1.5 : 1 + (PlayerState.MaxHp - p.Hp) * .4;
            case "WCG-406":
                var shrink = 300 * (HealedThisTurn(p) + 2);
                var kill = e.Field.Where(x => x.IsUnit && x.CurrentPP <= shrink).Select(BodyValue).DefaultIfEmpty(0).Max();
                return kill > 0 ? kill + 1 : 0.5;
            case "WCG-408":
                var dmg = (SpringCards(p) + 2) / 2;
                return dmg >= e.Hp ? 1000 : dmg * 1.2 - 1;
        }
        return StarPlayValue(p, c);
    }
    private static MonsterInstance? SpringTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) => pending.Title switch
    {
        SpringBaptismTitle => legal.OrderByDescending(x => x.CurrentPP).FirstOrDefault(),
        _ => StarTarget(p, pending, legal)
    };
}
