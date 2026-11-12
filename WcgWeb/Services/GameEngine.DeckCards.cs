using WcgWeb.Models;
namespace WcgWeb.Services;

// 指示物改為牌組裡的卡（2026-11-12）：persistent state is shown only with cards from the decks — cards played from hand, cards
// put under a monster or structure, or arrows. No external tokens.
// · 墊牌: 倒數／DP／血誓／聖泉 are face-down cards taken from the effect controller's deck bottom (never looked at) and put under
//   the card (MonsterInstance.Under); they go back to their owner's deck bottom when removed or when the card leaves play.
//   A short deck places what it can; placing 0 never counts as removing the last countdown card.
// · 聖盾: a monster has 聖盾 while it has a source: an attached 聖盾 spell, a hand card put face up under it (ShieldCards), or a
//   creature giving it 聖盾 with an arrow. When it is attacked one source goes to the graveyard and it isn't destroyed that combat.
// · 腐蝕: arrows and cards attached under enemy monsters (each source -300 PP, or what the card says).
// · 奪魂: 王座篡奪 itself is attached under the stolen monster.
public partial class GameEngine
{
    public bool DeckCardRules => CardBalanceHistory.DeckCardsCatalog(_cardDb);
    internal const string ShieldSourceTitle = "選擇進入墓地的聖盾來源";
    internal const string ShieldCardTitle = "選擇正面朝上放在怪物底下的手牌（聖盾來源）";
    internal const string ShieldCardTargetTitle = "選擇獲得聖盾的己方怪物";
    internal const string EnergyGuardTitle = "能量哨兵：要把能量區 1 張牌送入墓地，使它此次交戰不會被消滅嗎？";

    internal static string UnderLabel(UnderKind kind) => kind switch
    {
        UnderKind.Countdown => "倒數牌", UnderKind.Dp => "DP 牌", UnderKind.BloodOath => "血誓", _ => "聖泉"
    };
    internal static string UnderKey(UnderKind kind) => kind switch
    {
        UnderKind.Countdown => "countdown", UnderKind.Dp => "dp", UnderKind.BloodOath => "blood", _ => "spring"
    };
    // Put up to n face-down cards from the bottom of from's deck under m. Returns how many were placed.
    private int PadUnder(PlayerState from, MonsterInstance m, UnderKind kind, int n, CardDefinition? source = null)
    {
        if (!Alive(m) || n <= 0) return 0;
        var placed = 0;
        while (placed < n && from.Deck.Count > 0)
        {
            var c = from.Deck[^1]; from.Deck.RemoveAt(from.Deck.Count - 1);
            m.Under.Add(new(c, from.Id, kind)); placed++;
        }
        RefreshBoard();
        if (placed > 0)
        {
            Present("status", Owner(m), m.InstanceId, amount: placed, card: source ?? m.Card, label: $"從牌庫底墊 {placed} 張{UnderLabel(kind)}");
            Log($"【{m.Card.Name}】底下從牌庫底墊了 {placed} 張{UnderLabel(kind)}（共 {m.UnderCount(kind)} 張）。", "action");
        }
        else Log($"【{from.Name}】牌庫沒有牌，【{m.Card.Name}】底下沒有墊{UnderLabel(kind)}。", "action");
        return placed;
    }
    // Put up to n cards of this kind from under m back on their owners' deck bottoms (most recent first). Returns how many.
    private int ReturnUnder(MonsterInstance m, UnderKind kind, int n)
    {
        if (n <= 0) return 0;
        var picked = m.Under.Where(u => u.Kind == kind).Reverse().Take(n).ToList();
        foreach (var u in picked) { m.Under.Remove(u); PlayerById(u.OwnerId).Deck.Add(u.Card); }
        if (picked.Count > 0)
        {
            RefreshBoard();
            Present("status", Owner(m), m.InstanceId, amount: picked.Count, card: m.Card, label: $"{picked.Count} 張{UnderLabel(kind)}放回牌庫底（剩 {m.UnderCount(kind)}）");
        }
        return picked.Count;
    }
    // Leaving play (or silence for the listed kinds): the face-down cards go back to their owners' deck bottoms.
    private void ReturnAllUnder(MonsterInstance m, params UnderKind[] kinds)
    {
        foreach (var u in m.Under.Where(u => kinds.Length == 0 || kinds.Contains(u.Kind)).ToArray()) { m.Under.Remove(u); PlayerById(u.OwnerId).Deck.Add(u.Card); }
    }

    // ── DP ──
    private void AddDpCards(PlayerState from, MonsterInstance m, CardDefinition source, int n = 1)
    {
        if (!Alive(m) || !m.IsUnit || n <= 0) return;
        if (PadUnder(from, m, UnderKind.Dp, n, source) > 0) Log($"【{m.Card.Name}】DP {m.CurrentDP}。", "action");
    }

    // ── 聖盾 ──
    internal static bool ShieldSpell(string id) => id is "WCG-062" or "WCG-074" or "WCG-189" or "WCG-198" or "WCG-267";
    // Only these arrows hand out 聖盾 (147 only with ←); the other arrow cards link slots for their own effects.
    public static bool ShieldArrows(string id) => id is "WCG-061" or "WCG-073" or "WCG-147" or "WCG-063" or "WCG-031" or "WCG-069" or "WCG-075" or "WCG-079" or "WCG-080" or "WCG-171" or "WCG-179" or "WCG-240";
    internal sealed record ShieldSource(AttachedSpell? Card, MonsterInstance? Giver, bool Spell);
    // 聖盾 sources of a monster right now: cards under it first (attached spells, then hand cards), then the creatures giving it.
    private List<ShieldSource> ShieldSourcesOf(MonsterInstance m)
    {
        var list = new List<ShieldSource>();
        if (!CanShield(m)) return list;
        var p = Owner(m);
        if (Arbitrated(p, m)) return list;
        foreach (var a in m.Attachments.Where(a => ShieldSpell(a.Card.Card.Id))) list.Add(new(a, null, true));
        foreach (var a in m.ShieldCards) list.Add(new(a, null, false));
        var givers = Player.Field.Concat(Computer.Field).Where(s => s != m && s.IsUnit && !s.IsSilenced && ShieldArrows(s.Card.Id) && PointsAt(Owner(s), s, p, m)
            && !(s.Card.Id == "WCG-147" && Across(s, m))).ToList();
        // 356 連枝守衛: pointed at by 2 or more arrows, each creature pointing at it is a source.
        if (RootShield(p, m))
            givers.AddRange(Player.Field.Concat(Computer.Field).Where(s => s != m && s.IsUnit && !s.IsSilenced && s.Card.Arrows.Length > 0 && PointsAt(Owner(s), s, p, m) && !givers.Contains(s)));
        foreach (var g in givers.OrderBy(g => g.Card.TotalCost).ThenBy(g => g.Slot)) list.Add(new(null, g, false));
        return list;
    }
    internal int ShieldSourceCount(MonsterInstance m) => ShieldSourcesOf(m).Count;
    // 147's ↑ arrow: the monster it points at loses 聖盾 and can't gain it — cards under it that are 聖盾 sources go to the graveyard.
    private void PurgeShieldCards(MonsterInstance m, string reason)
    {
        var spells = m.Attachments.Where(a => ShieldSpell(a.Card.Card.Id)).ToArray();
        var cards = m.ShieldCards.ToArray();
        if (spells.Length + cards.Length == 0) return;
        foreach (var a in spells) { m.Attachments.Remove(a); PlayerById(a.OwnerId).Graveyard.Add(a.Card); }
        foreach (var a in cards) { m.ShieldCards.Remove(a); PlayerById(a.OwnerId).Graveyard.Add(a.Card); }
        Present("status", Owner(m), m.InstanceId, card: m.Card, label: "聖盾來源進入墓地");
        Log($"【{m.Card.Name}】底下的 {spells.Length + cards.Length} 張聖盾來源因{reason}進入墓地。", "action");
    }
    // Put a hand card face up under target as a 聖盾 source (「你可」: the player may decline).
    private void OfferShieldCard(PlayerState p, MonsterInstance target, CardDefinition source, Action? after = null)
    {
        if (!Alive(target) || !CanShield(target) || Arbitrated(Owner(target), target) || p.Hand.Count == 0 || IsOver) { after?.Invoke(); return; }
        var hand = p.Hand.ToArray();
        Ask(p, ShieldCardTitle, hand.Select(c => new ChoiceOption { Id = c.InstanceId.ToString(), Title = c.Card.Name, PreviewCard = c.Card, Subtitle = $"放在【{target.Card.Name}】底下" })
            .Append(new() { Id = "SKIP", Title = "不放" }), o =>
        {
            var c = o.Id == "SKIP" ? null : hand.FirstOrDefault(x => x.InstanceId.ToString() == o.Id);
            if (c != null && p.Hand.Contains(c) && Alive(target) && CanShield(target))
            {
                p.Hand.Remove(c); target.ShieldCards.Add(new(c, p.Id)); RefreshBoard();
                Present("attach", p, c.InstanceId, target.InstanceId, card: c.Card, label: "聖盾來源");
                Log($"【{p.Name}】把 1 張手牌正面朝上放在【{target.Card.Name}】底下（【{source.Name}】）：它獲得聖盾。", "action");
            }
            after?.Invoke();
        }, sourceInstanceId: target.InstanceId);
        if (CurrentPendingChoice != null) CurrentPendingChoice.SourceCard = source;
    }
    // Being attacked: one 聖盾 source goes to the graveyard (the controller picks when there are several) and the monster survives
    // this combat. 304 能量哨兵 may instead put 1 energy card into the graveyard when it has no 聖盾.
    private void SpendShield(MonsterInstance m, bool lethal, Action<bool> finish)
    {
        if (!Alive(m)) { finish(false); return; }
        var p = Owner(m);
        var sources = ShieldSourcesOf(m);
        if (sources.Count == 0)
        {
            if (!Active(m, "WCG-304") || p.EnergyZone.Count < 5) { finish(false); return; }
            Ask(p, EnergyGuardTitle, [new() { Id = "ENERGY", Title = "送 1 張能量入墓地" }, new() { Id = "SKIP", Title = "不使用" }], o =>
            {
                if (o.Id != "ENERGY" || !Alive(m) || p.EnergyZone.Count < 5) { finish(false); return; }
                var c = p.EnergyZone.Where(x => !x.TemporaryEnergy).OrderByDescending(x => x.IsTapped).FirstOrDefault() ?? p.EnergyZone[^1];
                p.EnergyZone.Remove(c); c.IsTapped = false; c.TemporaryEnergy = false; p.Graveyard.Add(c);
                Present("discard", p, c.InstanceId, card: c.Card, label: "能量送入墓地");
                Present("status", p, m.InstanceId, card: m.Card, label: "此次交戰不會被消滅");
                Log($"【{m.Card.Name}】把能量區 1 張牌送入墓地，此次交戰不會被消滅。", "combat");
                finish(true);
            }, sourceInstanceId: m.InstanceId);
            if (CurrentPendingChoice != null) { CurrentPendingChoice.SourceCard = m.Card; CurrentPendingChoice.Description = lethal ? "不使用的話，它會在這次交戰中被消滅。" : "這次交戰它本來就不會被消滅。"; }
            return;
        }
        void Spend(ShieldSource s)
        {
            if (s.Card is { } a)
            {
                if (s.Spell ? m.Attachments.Remove(a) : m.ShieldCards.Remove(a)) PlayerById(a.OwnerId).Graveyard.Add(a.Card);
                RefreshBoard();
                Present("status", p, m.InstanceId, card: a.Card.Card, label: "聖盾來源進入墓地");
                Log($"【{m.Card.Name}】被攻擊，底下的【{a.Card.Card.Name}】進入墓地：此次交戰不會被消滅。", "combat");
                finish(true);
                return;
            }
            var g = s.Giver!;
            Present("status", p, m.InstanceId, card: g.Card, label: "聖盾來源進入墓地");
            Log($"【{m.Card.Name}】被攻擊，給予聖盾的【{g.Card.Name}】進入墓地：此次交戰不會被消滅。", "combat");
            finish(true);
            KillBatch([g], "聖盾來源");
        }
        if (sources.Count == 1) { Spend(sources[0]); return; }
        var options = sources.Select((s, i) => new ChoiceOption
        {
            Id = "SRC:" + i, Title = s.Card != null ? s.Card.Card.Card.Name : s.Giver!.Card.Name, PreviewCard = s.Card != null ? s.Card.Card.Card : s.Giver!.Card,
            Subtitle = s.Card != null ? (s.Spell ? "底下的聖盾法術" : "底下的手牌") : "用箭頭給予聖盾的怪物（進入墓地）"
        }).ToList();
        Ask(p, ShieldSourceTitle, options, o =>
        {
            var now = ShieldSourcesOf(m);
            var pick = sources[int.Parse(o.Id.AsSpan(4))];
            if (!now.Any(x => x.Card == pick.Card && x.Giver == pick.Giver)) pick = now.FirstOrDefault() ?? pick;
            if (now.Count == 0) { finish(false); return; }
            Spend(pick);
        }, sourceInstanceId: m.InstanceId);
        if (CurrentPendingChoice != null) { CurrentPendingChoice.SourceCard = m.Card; CurrentPendingChoice.Description = $"【{m.Card.Name}】被攻擊：選 1 個聖盾來源進入墓地，它此次交戰不會被消滅。"; }
    }
    // AI: keep the strongest givers; spend cards under the monster first, then the cheapest giver.
    private static ChoiceOption? ShieldSourceChoice(PendingChoice choice) =>
        choice.Title != ShieldSourceTitle ? null : choice.Options.FirstOrDefault();
    private ChoiceOption? ShieldCardChoice(PlayerState p, PendingChoice choice)
    {
        if (choice.Title == EnergyGuardTitle) return choice.Options.First(o => o.Id == (choice.Description.StartsWith("不使用", StringComparison.Ordinal) ? "ENERGY" : "SKIP"));
        if (choice.Title != ShieldCardTitle) return ShieldSourceChoice(choice);
        var hand = choice.Options.Where(o => o.Id != "SKIP").Select(o => (o, c: p.Hand.FirstOrDefault(x => x.InstanceId.ToString() == o.Id))).Where(x => x.c != null).ToList();
        if (hand.Count < 2) return choice.Options.First(o => o.Id == "SKIP");
        return hand.OrderBy(x => (x.c!.Card.IsCounter ? 3 : 0) + HandValue(p, x.c!)).ThenBy(x => x.c!.Card.Id, StringComparer.Ordinal).First().o;
    }

    // ── 腐蝕 ──
    // Cards of this archetype that reduce PP while attached under an enemy monster.
    internal static bool CorrosionAttachment(string id) => id is "WCG-382" or "WCG-387" or "WCG-388";
    // Your arrows pointing at the monster (any of your arrow cards, monsters or structures).
    private int YourArrowsAt(PlayerState you, MonsterInstance x) =>
        you.Board.Count(s => s != x && !s.IsSet && !(s.IsUnit && s.IsSilenced) && s.Card.Arrows.Length > 0 && PointsAt(you, s, Owner(x), x));
    private static int YourCardsUnder(PlayerState you, MonsterInstance x) => x.Attachments.Count(a => a.OwnerId == you.Id);
    // Attach a card that is in a graveyard (a dead 382／387 or a resolved 388) under the target; it stays its owner's card.
    private void AttachFromGraveyard(CardInstance card, MonsterInstance target, CardDefinition source)
    {
        if (!Alive(target)) return;
        var holder = Player.Graveyard.Contains(card) ? Player : Computer.Graveyard.Contains(card) ? Computer : null;
        if (holder == null) return;
        holder.Graveyard.Remove(card); target.Attachments.Add(new(card, holder.Id)); RefreshBoard();
        Present("attach", holder, card.InstanceId, target.InstanceId, card: source, label: "附著");
        Log($"【{source.Name}】附著於【{target.Card.Name}】底下（PP {target.CurrentPP}）。", "action");
    }
}
