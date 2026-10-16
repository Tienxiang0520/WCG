using WcgWeb.Models;

namespace WcgWeb.Services;

public partial class GameEngine
{
    // Catalog wording also selects the rules for old ranked journals. No existing save is rewritten.
    public bool DamageCounters => _cardDb.GetCard("WCG-195")?.Text.Contains("受到", StringComparison.Ordinal) == true;
    private sealed record DamageSource(PlayerState Owner, CardDefinition Card, MonsterInstance? Unit, string Kind);
    private sealed class DamageContext(PlayerState recipient, int lostHp, DamageSource? source)
    {
        public PlayerState Recipient { get; } = recipient;
        public int LostHp { get; } = lostHp;
        public DamageSource? Source { get; } = source;
        public bool Reversed { get; set; }
    }
    private int _reactionDepth;

    private bool CanDestroyDamageSource(DamageContext damage) => damage.Source?.Unit is { } unit
        && unit.IsUnit && Alive(unit) && (unit.IsSilenced || unit.Card.Id != "WCG-152");
    private bool CanRecoverCounter(PlayerState p) => p.Graveyard.Any(c => c.Card.IsMonster && c.Card.HasWill("深淵"))
        && p.Graveyard.Any(c => c.Card.IsMonster && c.Card.TotalCost <= 2);
    private bool CanReact(PlayerState p, CardInstance card, DamageContext damage)
    {
        if (!p.Hand.Contains(card) || !card.Card.IsCounter || damage.LostHp <= 0 || damage.Reversed
            || !CanPayCost(p, card.Card, out _)) return false;
        if (SimplifiedRules)
        {
            // Damage dealt by a counter card cannot be countered: no chains.
            if (damage.Source?.Card.IsCounter == true) return false;
            var monster = damage.Source?.Card.IsMonster == true;
            return card.Card.Id switch
            {
                "WCG-195" or "WCG-199" => monster,
                "WCG-196" => damage.Source is { } s && s.Owner != p,
                "WCG-197" or "WCG-198" => true,
                _ => false
            };
        }
        var enemySource = damage.Source is { } source && source.Owner != p;
        var enemyMonster = enemySource && damage.Source!.Card.IsMonster;
        return card.Card.Id switch
        {
            "WCG-195" => enemyMonster && CanDestroyDamageSource(damage),
            "WCG-196" or "WCG-197" => enemySource,
            "WCG-198" => true,
            "WCG-199" => enemyMonster && (CanDestroyDamageSource(damage) || CanRecoverCounter(p)),
            _ => false
        };
    }
    private void DamageReaction(DamageContext damage)
    {
        var p = damage.Recipient;
        var cards = p.Hand.Where(c => CanReact(p, c, damage)).ToArray();
        // No invalid-choice popup, no automatic discard and no inspection of an opponent's hand.
        if (cards.Length == 0) return;
        Ask(p, "受到傷害，可從手牌發動反擊", cards.Select(c => new ChoiceOption
        {
            Id = c.InstanceId.ToString(), Title = c.Card.Name, PreviewCard = c.Card,
            Subtitle = $"支付 {ActualCost(p,c.Card)} 點能量"
        }).Append(new() { Id = "SKIP", Title = "略過，不發動反擊" }), option =>
        {
            if (option.Id == "SKIP") return;
            var card = cards.First(c => c.InstanceId.ToString() == option.Id);
            if (!CanReact(p, card, damage)) return;
            CanPayCost(p, card.Card, out var payment);
            foreach (var energy in payment) energy.IsTapped = true;
            p.Hand.Remove(card); p.Graveyard.Add(card);
            Present("pay", p, amount: payment.Count, label: "支付能量");
            Present("play", p, card.InstanceId, card: card.Card, label: "從手牌發動反擊");
            Log($"【{p.Name}】從手牌發動【{card.Card.Name}】。", "action");
            // Re-check remaining hand cards only after this counter's full effect and nested reactions.
            // Simplified rules: at most 1 counter per damage event.
            Resolve(() => ResolveDamageCounter(p, card, damage), () => { if (!SimplifiedRules) DamageReaction(damage); });
            UsedSpell(p, card.Card);
        }, sourceInstanceId: damage.Source?.Unit?.InstanceId);
        CurrentPendingChoice!.Description = damage.Source == null ? "生命傷害已扣除；反擊結算後才判定敗北。"
            : $"傷害來源：【{damage.Source.Card.Name}】。生命傷害已扣除；只列出符合卡文條件且付得起費用的手牌。";
    }
    private void ReverseDamage(DamageContext damage)
    {
        if (damage.Reversed) return;
        damage.Reversed = true;
        // Reversal restores only the HP actually lost in this damage event, once. Other effects continue.
        Heal(damage.Recipient, damage.LostHp);
        Present("status", damage.Recipient, label: "抵銷這次傷害");
    }
    private void ResolveDamageCounter(PlayerState p, CardInstance card, DamageContext damage)
    {
        var enemy = GetOpponent(p); var source = damage.Source?.Unit;
        if (SimplifiedRules && card.Card.Id is "WCG-195" or "WCG-199")
        {
            // If the monster already left the board, the counter only cancels the damage.
            var killable = source != null && CanDestroyDamageSource(damage); var pp = killable ? source!.CurrentPP : 0;
            Resolve(() => ReverseDamage(damage), () => { if (killable) KillBatch([source!], card.Card.Name); },
                () => { if (card.Card.Id == "WCG-195") { if (killable && pp >= 1800) SpellDamage(p, enemy, 1, card.Card); } else if (CanRecoverCounter(p)) Recover(p, 2, 1, optional: true); });
            return;
        }
        switch (card.Card.Id)
        {
            case "WCG-195":
                if (source == null || !CanDestroyDamageSource(damage)) return;
                var power = source.CurrentPP;
                Resolve(() => ReverseDamage(damage), () => KillBatch([source], card.Card.Name),
                    () => { if (power >= 1500) SpellDamage(p, enemy, 1, card.Card); });
                break;
            case "WCG-196":
                Resolve(() => ReverseDamage(damage), () => DrawMany(p, 1));
                break;
            case "WCG-197":
                Resolve(() => ReverseDamage(damage), () => FreeFaction(p, 3, "生機"));
                break;
            case "WCG-198":
                Resolve(() => ReverseDamage(damage), () => Heal(p, 2),
                    () => OptionalTarget(p, SimplifiedRules ? "可附著聖盾" : "可附著能量護盾", p.Field.Where(x => x.IsSilenced || x.Card.Id != "WCG-152"), x => Attach(p, card, x)));
                break;
            case "WCG-199":
                Resolve(() =>
                {
                    if (source != null && CanDestroyDamageSource(damage))
                    { Resolve(() => ReverseDamage(damage), () => KillBatch([source], card.Card.Name)); }
                }, () => { if (CanRecoverCounter(p)) Recover(p, 2, 1, optional: true); });
                break;
        }
    }
    private void FinishDamageReaction(DamageContext damage)
    {
        _reactionDepth--;
        if (_reactionDepth != 0) return;
        CheckLethal(damage.Recipient);
        if (!IsOver) CheckLethal(GetOpponent(damage.Recipient));
    }
}
