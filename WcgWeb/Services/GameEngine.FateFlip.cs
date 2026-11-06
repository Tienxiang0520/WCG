using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「命運翻牌」（深淵＋中立，WCG-418–426，2026-10-25）：翻開牌庫頂，依類型與印刷費用給不同效果。
// 翻開: show the top card of your deck to both players, resolve the effect, and put it on the bottom of your deck (it is not
// drawn and never goes to the graveyard). With an empty deck nothing is revealed and the effect is skipped (no loss).
// The card goes to the bottom first, so a "draw 1" in the same effect draws the next card. 424／426 may summon the revealed
// monster from the deck for free (its deploy doesn't trigger). Only catalogs from that day contain these ids.
public partial class GameEngine
{
    internal const string FateWagerTitle = "深淵賭約：選擇敵方怪物（本回合 PP 降低）";
    internal const string FateWellTitle = "開眼之井：要失去 1 點生命，把翻開的牌加入手牌嗎？";

    private void Reveal(PlayerState p, CardDefinition source, Action<CardInstance> resolve)
    {
        if (IsOver) return;
        if (p.Deck.Count == 0) { Log($"【{p.Name}】牌庫沒有牌，【{source.Name}】不翻開。"); return; }
        var c = p.Deck[0]; p.Deck.RemoveAt(0); p.Deck.Add(c);
        Present("reveal", p, c.InstanceId, card: c.Card, label: "翻開牌庫頂");
        Log($"【{p.Name}】翻開牌庫頂【{c.Card.Name}】（{source.Name}），放到牌庫底。", "action");
        var actions = new List<Action> { () => resolve(c) };
        // 423 宿命拾荒者 and 425 命運祭壇 react to every reveal.
        foreach (var s in p.Field.Where(x => Active(x, "WCG-423")).ToArray())
            actions.Add(() =>
            {
                if (!Alive(s) || !Active(s, "WCG-423")) return;
                if (c.Card.IsSpell) AddDpCounter(s, s.Card);
                else if (c.Card.IsMonster) AddTurnBonus(s, 300, s.Card);
            });
        if (c.Card.TotalCost >= 5)
            foreach (var altar in p.Structures.Where(x => !x.IsSet && x.Card.Id == "WCG-425").ToArray())
                actions.Add(() => { if (!p.Structures.Contains(altar)) return; Present("trigger", p, altar.InstanceId, card: altar.Card, label: altar.Card.Name); Damage(GetOpponent(p), 1, new(p, altar.Card, null, "passive")); });
        Resolve(actions.ToArray());
    }
    // Summon a revealed monster from the deck for free (no deploy); false if it isn't there or there's no empty slot.
    private bool SummonFromDeck(PlayerState p, CardInstance c)
    {
        if (!c.Card.IsMonster || !p.Deck.Contains(c) || p.Occupied >= 5) return false;
        p.Deck.Remove(c); Summon(p, c, false); return true;
    }

    private bool FateDeploy(PlayerState p, MonsterInstance m)
    {
        switch (m.Card.Id)
        {
            case "WCG-418": Reveal(p, m.Card, c => { if (c.Card.IsMonster) DrawMany(p, 1); }); break;
            case "WCG-422": Reveal(p, m.Card, c => { if (c.Card.TotalCost >= 4 && Alive(m) && !m.IsSilenced) KillBatch(PointedUnits(m).ToArray(), m.Card.Name); }); break;
            case "WCG-424": Reveal(p, m.Card, c => { if (!SummonFromDeck(p, c)) DrawMany(p, 1); }); break;
            default: return false;
        }
        return true;
    }
    // 419 命輪占者: reveal at the start of your turn.
    private void FateStartTurn(PlayerState p)
    {
        foreach (var m in p.Field.Where(x => Active(x, "WCG-419")).ToArray())
            _effects.AddLast(() =>
            {
                if (!Alive(m) || !Active(m, "WCG-419") || IsOver) return;
                Present("trigger", p, m.InstanceId, card: m.Card, label: m.Card.Name);
                Reveal(p, m.Card, c =>
                {
                    if (c.Card.IsMonster) { if (Alive(m)) AddTurnBonus(m, 500, m.Card); }
                    else if (c.Card.IsSpell) Damage(GetOpponent(p), 1, new(p, m.Card, m, "passive"));
                    else if (c.Card.IsEnchantment) Heal(p, 1);
                });
            });
    }
    private bool FateSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        var e = GetOpponent(p);
        switch (source.Card.Id)
        {
            case "WCG-420":
                Reveal(p, source.Card, c => PickMonster(p, FateWagerTitle, SpellPickable(p, e.Field.Where(x => x.IsUnit)), x => ShrinkUntilEndOfTurn(x, 300 * c.Card.TotalCost, source.Card)));
                break;
            case "WCG-426":
                var steps = Enumerable.Range(0, 4).Select(_ => (Action)(() => Reveal(p, source.Card, c =>
                {
                    if (c.Card.IsMonster) { if (!SummonFromDeck(p, c) && p.Deck.Remove(c)) { p.Hand.Add(c); Present("take", p, c.InstanceId, card: c.Card, label: "加入手牌"); } }
                    else if (c.Card.IsSpell) Damage(e, 1, new(p, source.Card, null, "spell"));
                    else if (c.Card.IsEnchantment) DrawMany(p, 1);
                }))).ToArray();
                Resolve(steps);
                break;
            default: return false;
        }
        RefreshBoard();
        return true;
    }
    private string FatePlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-420" when !GetOpponent(p).Field.Any(x => x.IsUnit) => "敵方場上沒有怪物。",
        _ => ""
    };
    // 421 開眼之井: tap, reveal, and optionally pay 1 life to take the card.
    private static bool FateCanActivate(PlayerState p, MonsterInstance s) => s.Card.Id == "WCG-421" && p.Deck.Count > 0;
    private void FateActivate(PlayerState p, MonsterInstance s)
    {
        if (s.Card.Id != "WCG-421") return;
        s.IsTapped = true;
        Reveal(p, s.Card, c => Ask(p, FateWellTitle, [new() { Id = "TAKE", Title = "失去 1 點生命，加入手牌", PreviewCard = c.Card }, new() { Id = "SKIP", Title = "放回牌庫底" }], o =>
        {
            if (o.Id != "TAKE" || !p.Deck.Remove(c)) return;
            PayLife(p, 1); if (IsOver) return;
            p.Hand.Add(c); Present("take", p, c.InstanceId, card: p == Player ? c.Card : null, label: "加入手牌");
        }));
    }

    // ── 天梯電腦 ──
    private double? FatePlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        switch (c.Card.Id)
        {
            case "WCG-420":
                var avg = p.Deck.Count == 0 ? 0 : p.Deck.Average(x => x.Card.TotalCost) * 300;
                var hit = e.Field.Where(x => x.IsUnit && x.CurrentPP <= avg).Select(BodyValue).DefaultIfEmpty(0).Max();
                return hit > 0 ? hit : 0.3;
            case "WCG-426": return p.Deck.Count == 0 ? -5 : 6;
        }
        return null;
    }
    private static MonsterInstance? FateTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) => pending.Title switch
    {
        FateWagerTitle => legal.OrderByDescending(x => x.CurrentPP).FirstOrDefault(),
        _ => null
    };
    private static ChoiceOption? FateChoice(PlayerState p, PendingChoice choice) =>
        choice.Title != FateWellTitle ? null : choice.Options.First(o => o.Id == (p.Hp >= 4 ? "TAKE" : "SKIP"));
}
