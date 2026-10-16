using WcgWeb.Models;

namespace WcgWeb.Services;

// 理智 buff (2026-10-17). Card data selects the era: before it, 秘法護衛 (251) had no taunt, so old ranked
// journals and match codes replay with the old 023/032/037/229/231/251 behaviour and without 258–262.
// Counters 259/260 reuse the hand-counter rule: the reacting player reveals the card from hand and pays with
// untapped energy; at most 1 counter per event, and a counter can't be countered. Nothing is remembered.
public partial class GameEngine
{
    internal bool ReasonBuff => _cardDb.GetCard("WCG-251")?.HasTaunt == true;
    internal const string NegateTitle = "敵方施放法術，可從手牌發動反擊";
    internal const string EbbTideTitle = "敵方付費召喚怪物，可從手牌發動反擊";
    internal const string ScryTitle = "檢視牌庫頂 1 張";

    private bool AltarReady(PlayerState p) => ReasonBuff ? p.Field.Count <= GetOpponent(p).Field.Count : p.Field.Count == 1;

    // 靈光學徒 after the buff: look at the top card, may put it on the bottom. Only its owner sees the card.
    private void ScryOne(PlayerState p)
    {
        if (p.Deck.Count == 0) return;
        var top = p.Deck[0];
        Ask(p, ScryTitle, [new() { Id = "TOP", Title = "留在牌庫頂", PreviewCard = top.Card }, new() { Id = "BOTTOM", Title = "放到牌庫底", PreviewCard = top.Card }], o =>
        {
            if (o.Id != "BOTTOM" || p.Deck.Count == 0 || p.Deck[0] != top) return;
            p.Deck.RemoveAt(0); p.Deck.Add(top); Log($"【{p.Name}】把牌庫頂 1 張放到牌庫底。");
        });
    }

    private CardInstance[] Counters(PlayerState reactor, string id) =>
        reactor.Hand.Where(c => c.Card.Id == id && c.Card.IsCounter && CanPayCost(reactor, c.Card, out _)).ToArray();

    private void PayCounter(PlayerState p, CardInstance card)
    {
        CanPayCost(p, card.Card, out var payment);
        foreach (var energy in payment) energy.IsTapped = true;
        p.Hand.Remove(card); p.Graveyard.Add(card);
        Present("pay", p, amount: payment.Count, label: "支付能量");
        Present("play", p, card.InstanceId, card: card.Card, label: "從手牌發動反擊");
        Log($"【{p.Name}】從手牌發動【{card.Card.Name}】。", "action");
    }

    private void AskCounter(PlayerState reactor, string title, CardInstance[] cards, string description, Action<CardInstance> use, Action skip, Guid? source)
    {
        Ask(reactor, title, cards.Select(c => new ChoiceOption { Id = c.InstanceId.ToString(), Title = c.Card.Name, PreviewCard = c.Card, Subtitle = $"支付 {ActualCost(reactor, c.Card)} 點能量" })
            .Append(new() { Id = "SKIP", Title = "略過，不發動反擊" }), option =>
        {
            var card = option.Id == "SKIP" ? null : cards.FirstOrDefault(c => c.InstanceId.ToString() == option.Id);
            if (card == null || !reactor.Hand.Contains(card) || !CanPayCost(reactor, card.Card, out _)) { skip(); return; }
            PayCounter(reactor, card); use(card);
        }, sourceInstanceId: source);
        CurrentPendingChoice!.Description = description;
    }

    // 否決之言 (259): the enemy's spell is already paid and in the graveyard; if negated it has no effect and
    // does not count as cast for "每當你施放" abilities. Returns false when no window opens (cast continues at once).
    private bool OfferNegate(PlayerState caster, CardInstance spell, Action cast)
    {
        var reactor = GetOpponent(caster); var cards = Counters(reactor, "WCG-259");
        if (cards.Length == 0 || spell.Card.IsCounter) return false;
        AskCounter(reactor, NegateTitle, cards, $"對手施放【{spell.Card.Name}】（已付費）。發動後該法術無效並留在墓地。", card =>
        {
            Present("status", caster, spell.InstanceId, card: spell.Card, label: "法術被無效");
            Log($"【{spell.Card.Name}】被【{card.Card.Name}】無效。", "action");
            Resolve(() => DrawMany(reactor, 1), () => UsedSpell(reactor, card.Card));
        }, cast, spell.InstanceId);
        return true;
    }

    // 退潮令 (260): after a paid summon and before its entry ability, the reacting player may bounce it.
    private void OfferEbbTide(PlayerState summoner, MonsterInstance m)
    {
        var reactor = GetOpponent(summoner); var cards = Counters(reactor, "WCG-260");
        if (cards.Length == 0) return;
        AskCounter(reactor, EbbTideTitle, cards, $"對手付費召喚【{m.Card.Name}】。發動後它回到持有者手上，進場效果不發動。", card =>
        {
            Resolve(() => Bounce(m), () => UsedSpell(reactor, card.Card));
        }, () => { }, m.InstanceId);
    }
}
