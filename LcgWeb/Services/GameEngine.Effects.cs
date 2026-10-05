using LcgWeb.Models;
namespace LcgWeb.Services;

public partial class GameEngine
{
    private void Deploy(PlayerState p, MonsterInstance m)
    {
        if (!Alive(m) || m.IsSilenced) return;
        var enemy = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "LCG-001": case "LCG-102":
                var modes = new List<ChoiceOption> { new() { Id = "LOOT", Title = "抽 1 張，然後棄 1 張" } };
                if (Targetable(p, enemy.Field.Where(x => x.CurrentPP <= 500)).Count > 0)
                    modes.Insert(0, new() { Id = "KILL", Title = "消滅 1 隻 PP 500 以下敵怪" });
                Ask(p, $"【{m.Card.Name}】進場抉擇", modes, o =>
                { if (o.Id == "LOOT") Loot(p); else PickMonster(p, "選擇 PP 500 以下敵怪", enemy.Field.Where(x => x.CurrentPP <= 500), x => KillBatch(new[] { x }, m.Card.Name)); });
                break;
            case "LCG-007": PickLowest(p, enemy.Field.Where(x => x.CurrentPP <= 500), x => KillBatch(new[] { x }, m.Card.Name)); break;
            case "LCG-013": Resolve(() => Damage(enemy, 1), () => Damage(p, 1)); break;
            case "LCG-015": if (enemy.Field.Count > p.Field.Count) DrawMany(p, 1); break;
            case "LCG-020": KillBatch(enemy.Field.Where(x => x.CurrentPP <= 1325).ToArray(), m.Card.Name); break;
            case "LCG-021": case "LCG-116": DrawMany(p, 1); break;
            case "LCG-027": case "LCG-047": case "LCG-115": FreeHand(p, 1, 1); break;
            case "LCG-067": case "LCG-104": PickMonster(p, "選擇費用 2 以下敵怪回手", enemy.Field.Where(x => x.Card.TotalCost <= 2), Bounce); break;
            case "LCG-033": if (enemy.Hand.Count > p.Hand.Count) DrawMany(p, 1); break;
            case "LCG-035": foreach (var x in enemy.Field.ToArray()) Freeze(x); break;
            case "LCG-039": DrawMany(p, 2); break;
            case "LCG-040":
                if (p == Player) { RevealedCards = enemy.Hand.Select(VisibleCard).ToArray(); RevealTitle = "檢視時的對手手牌"; }
                Log($"【{m.Card.Name}】檢視對手手牌：{string.Join("、", enemy.Hand.Select(c => c.Card.Name))}");
                CardChoice(p, "選擇對手手牌中的法術棄掉", enemy.Hand.Where(c => c.Card.IsSpell), c => { enemy.Hand.Remove(c); enemy.Graveyard.Add(c); }); break;
            case "LCG-041":
                Ask(p, "利爪巨鹿進場抉擇", new[] { new ChoiceOption { Id = "HEAL", Title = "回復 1 點生命" }, new ChoiceOption { Id = "LOOT", Title = "抽 1 張，棄 1 張" } },
                    o => { if (o.Id == "HEAL") Heal(p, 1); else Loot(p); }); break;
            case "LCG-045": if (p.Field.Any(x => x != m)) Loot(p); break;
            case "LCG-061": GrantShields(p, m, 1); break;
            case "LCG-073": GrantShields(p, m, 2); break;
            case "LCG-057": case "LCG-075": case "LCG-106": Heal(p, 1); break;
            case "LCG-077": PickMonster(p, "選擇 PP 2325 以上敵怪", enemy.Field.Where(x => x.CurrentPP >= 2325), x => KillBatch(new[] { x }, m.Card.Name)); break;
            case "LCG-085": Damage(p, 1); break;
            case "LCG-087": if (p.Graveyard.Count >= 5) Heal(p, 1); break;
            case "LCG-091": KillBatch(enemy.Field.Where(x => x.CurrentPP <= 500).ToArray(), m.Card.Name); break;
            case "LCG-100": Resolve(() => Heal(p, 2), () => Recover(p, int.MaxValue, 1)); break;
            case "LCG-105": KillBatch(p.Field.Concat(enemy.Field).Where(x => x != m && x.CurrentPP <= 500).ToArray(), m.Card.Name); break;
            case "LCG-112": PickMonster(p, "選擇費用 2 以下敵怪", enemy.Field.Where(x => x.Card.TotalCost <= 2), x => KillBatch(new[] { x }, m.Card.Name)); break;
            case "LCG-117": PickLowest(p, enemy.Field.Where(x => x.CurrentPP <= 1000), x => KillBatch(new[] { x }, m.Card.Name)); break;
            // Remaining monsters are printed keywords, plain stats, continuous/event or death abilities.
        }
    }
    private void FreeHand(PlayerState p, int maxCost, int count)
    {
        if (count <= 0 || p.Field.Count >= 5 || IsOver) return;
        CardChoice(p, $"可從手牌免費召喚費用 {maxCost} 以下怪物", p.Hand.Where(c => c.Card.IsMonster && c.Card.TotalCost <= maxCost), c =>
        {
            if (!p.Hand.Remove(c) || p.Field.Count >= 5) return;
            Summon(p, c, false); FreeHand(p, maxCost, count - 1);
        }, true);
    }
    private void Recover(PlayerState p, int maxCost, int count, CardInstance? exclude = null, bool optional = false)
    {
        if (count <= 0) return;
        CardChoice(p, "選擇墓地怪物加入手牌", p.Graveyard.Where(c => c != exclude && c.Card.IsMonster && c.Card.TotalCost <= maxCost), c =>
        { if (p.Graveyard.Remove(c)) { p.Hand.Add(c); Present("recover", p, c.InstanceId, card: c.Card, label: "墓地回收"); } Recover(p, maxCost, count - 1, exclude, optional); }, optional);
    }
    private void Revive(PlayerState p, int maxCost, CardInstance? exclude = null, bool optional = false)
    {
        if (p.Field.Count >= 5) return;
        CardChoice(p, "選擇墓地怪物非付費召喚", p.Graveyard.Where(c => c != exclude && c.Card.IsMonster && c.Card.TotalCost <= maxCost), c =>
        { if (p.Field.Count < 5 && p.Graveyard.Remove(c)) Summon(p, c, false); }, optional);
    }
    private void Search(PlayerState p, int count, Func<CardInstance, bool> qualifies, int take = 1, bool optional = false)
    {
        var top = p.Deck.Take(count).ToList(); p.Deck.RemoveRange(0, top.Count);
        if (p == Player) { RevealedCards = top.Select(VisibleCard).ToArray(); RevealTitle = "本次檢視的牌庫卡片"; }
        var candidates = top.Where(qualifies).ToList();
        if (candidates.Count == 0) { OrderBottom(p, top); return; }
        void Choose(int left)
        {
            var available = top.Where(qualifies).ToList();
            if (left == 0 || available.Count == 0) { OrderBottom(p, top); return; }
            var options = available.Select(c => new ChoiceOption { Id = c.InstanceId.ToString(), Title = c.Card.Name, PreviewCard = c.Card }).ToList();
            if (take > 1 || optional) options.Add(new() { Id = "SKIP", Title = "不再取得卡片" });
            Ask(p, "選擇檢視的卡片加入手牌", options, o =>
            {
                if (o.Id == "SKIP") { OrderBottom(p, top); return; }
                var c = available.First(c => c.InstanceId.ToString() == o.Id); top.Remove(c); p.Hand.Add(c); Present("take", p, instance: p == Player ? c.InstanceId : null, card: p == Player ? c.Card : null, label: "檢視取得卡牌"); Choose(left - 1);
            });
        }
        Choose(take);
    }
    private void OrderBottom(PlayerState p, List<CardInstance> remaining)
    {
        if (remaining.Count <= 1) { p.Deck.AddRange(remaining); return; }
        var options = remaining.Select(c => new ChoiceOption { Id = c.InstanceId.ToString(), Title = $"先放到牌庫底：{c.Card.Name}", PreviewCard = c.Card }).ToList();
        options.Insert(0, new() { Id = "KEEP", Title = "剩餘卡依目前順序放到牌庫底" });
        Ask(p, "排列牌庫底順序", options, o =>
        {
            if (o.Id == "KEEP") { p.Deck.AddRange(remaining); return; }
            var c = remaining.First(c => c.InstanceId.ToString() == o.Id); remaining.Remove(c); p.Deck.Add(c); OrderBottom(p, remaining);
        });
    }
    private void Spell(PlayerState p, CardInstance source, MonsterInstance? target, string mode)
    {
        var enemy = GetOpponent(p);
        var c = source.Card;
        void KillTarget() { if (target != null && SpellTargets(p, c).Contains(target)) KillBatch(new[] { target }, c.Name); }
        switch (c.Id)
        {
            case "LCG-004": case "LCG-048": Resolve(KillTarget, () => DrawMany(p, 1)); break;
            case "LCG-006": Damage(enemy, 2); break;
            case "LCG-014": Damage(enemy, 1); break;
            case "LCG-046": if (mode == "KILL") KillTarget(); else Loot(p); break;
            case "LCG-054": Search(p, 5, x => x.Card.IsMonster, 2); break;
            case "LCG-062": case "LCG-074":
                Resolve(() => { if (target != null && CanShield(target)) Shield(p, target); },
                    () => { if (c.Id == "LCG-062") Heal(p, 1); else DrawMany(p, 1); }); break;
            case "LCG-066": Resolve(() => Heal(p, 2), () => DrawMany(p, 1)); break;
            case "LCG-070": Heal(p, 1); break;
            case "LCG-072": Resolve(() => { if (target != null && SpellTargets(p, c).Contains(target)) Silence(p, source, target); }, () => DrawMany(p, 1)); break;
            case "LCG-002": Resolve(KillTarget, () => { if (p.Hp <= 3) Damage(enemy, 1); }); break;
            case "LCG-008": KillBatch(p.Field.Concat(enemy.Field).Where(m => m.CurrentPP <= 500).ToArray(), c.Name); break;
            case "LCG-010": DrawMany(p, Math.Min(2, p.Field.Count)); break;
            case "LCG-064": case "LCG-012": case "LCG-032": case "LCG-076": case "LCG-082": case "LCG-084": case "LCG-088": KillTarget(); break;
            case "LCG-017": case "LCG-123": Resolve(KillTarget, () => Damage(enemy, 1)); break;
            case "LCG-019": Resolve(() => Damage(p, 2), () => DrawMany(p, 2)); break;
            case "LCG-022": Search(p, 3, x => x.Card.IsSpell, optional: true); break;
            case "LCG-024": if (target != null && SpellTargets(p, c).Contains(target)) Freeze(target); break;
            case "LCG-026": if (target != null && SpellTargets(p, c).Contains(target)) Bounce(target); break;
            case "LCG-028": case "LCG-096": DrawMany(p, 2); break;
            case "LCG-030": Search(p, 3, _ => true); break;
            case "LCG-034": DrawMany(p, 3); break;
            case "LCG-036": Resolve(() => { if (target != null && SpellTargets(p, c).Contains(target)) Bounce(target); }, () => DrawMany(p, 1)); break;
            case "LCG-038": foreach (var m in p.Field.Concat(enemy.Field).Where(m => m.Card.TotalCost <= 2).ToArray()) Bounce(m); break;
            case "LCG-042":
                var options = new List<ChoiceOption> { new() { Id = "DRAW", Title = "抽 1 張牌" } };
                if (p.Field.Count < 5 && p.Hand.Any(x => x.Card.IsMonster && x.Card.TotalCost <= 1)) options.Add(new() { Id = "SUMMON", Title = "從手牌免費召喚 1 費以下怪物" });
                Ask(p, "野性之力抉擇", options, o => { if (o.Id == "DRAW") DrawMany(p, 1); else FreeHand(p, 1, 1); }); break;
            case "LCG-044":
                if (p.Deck.Count > 0) { var energy = p.Deck[0]; p.Deck.RemoveAt(0); energy.IsTapped = true; p.EnergyZone.Add(energy); Present("energy", p, energy.InstanceId, label: "額外背面能量"); Log("滋養萌發：額外能量背面橫置進場。"); } break;
            case "LCG-050": Heal(p, 2); break;
            case "LCG-052": FreeHand(p, 2, 2); break;
            case "LCG-056": Search(p, 3, x => x.Card.IsMonster && x.Card.TotalCost <= 2); break;
            case "LCG-058": Recover(p, 2, 2, optional: true); break;
            case "LCG-068": KillBatch(enemy.Field.Where(m => m.CurrentPP <= 675).ToArray(), c.Name); break;
            case "LCG-078": Resolve(() => KillBatch(enemy.Field.Where(m => m.CurrentPP <= 675).ToArray(), c.Name), () => Heal(p, 1)); break;
            case "LCG-086": Resolve(KillTarget, () => DrawMany(p, 1)); break;
            case "LCG-090": Recover(p, int.MaxValue, 1); break;
            case "LCG-092": KillBatch(p.Field.Concat(enemy.Field).Where(m => m.CurrentPP <= 825).ToArray(), c.Name); break;
            case "LCG-094": Resolve(KillTarget, () => Heal(p, 1)); break;
            case "LCG-098": KillBatch(p.Field.Concat(enemy.Field).ToArray(), c.Name); break;
            case "LCG-121": DrawMany(p, 1); break;
            case "LCG-122": Search(p, 5, x => x.Card.IsMonster); break;
            default: throw new InvalidOperationException($"Unimplemented spell {c.Id}");
        }
    }
    private void KillBatch(IEnumerable<MonsterInstance> monsters, string reason, bool destroyed = true)
    {
        var victims = monsters.Where(Alive).Distinct().ToList(); if (victims.Count == 0) return;
        // Snapshot listeners before simultaneous removal: no half-dead board or repeated death.
        var listeners = Player.Field.Where(m => !m.IsSilenced).Select(m => (Player, m)).Concat(Computer.Field.Where(m => !m.IsSilenced).Select(m => (Computer, m))).ToArray();
        var deaths = new List<(PlayerState owner, MonsterInstance monster, CardInstance grave)>();
        foreach (var m in victims)
        {
            var owner = Owner(m); ReleaseAttachments(m); owner.Field.Remove(m); var grave = new CardInstance(m.Card) { InstanceId = m.InstanceId }; owner.Graveyard.Add(grave);
            deaths.Add((owner, m, grave)); Present("death", owner, m.InstanceId, card: m.Card, label: reason); Log($"【{m.Card.Name}】因 {reason} 送入墓地。", "combat");
        }
        var triggers = new List<Action>();
        foreach (var (owner, m, grave) in deaths)
        {
            triggers.Add(() => Death(owner, m, grave));
            foreach (var (listenerOwner, listener) in listeners)
            {
                if (listener == m || !destroyed) continue;
                if (listener.Card.Id == "LCG-011" && ReferenceEquals(listenerOwner, owner)) triggers.Add(() => { Present("trigger", listenerOwner, listener.InstanceId, card: listener.Card, label: "死亡觸發"); DrawMany(listenerOwner, 1); });
                if (listener.Card.Id == "LCG-099") triggers.Add(() => { Present("trigger", listenerOwner, listener.InstanceId, card: listener.Card, label: "死亡觸發"); Heal(listenerOwner, 1); });
            }
        }
        Resolve(triggers.ToArray());
    }
    private void Death(PlayerState p, MonsterInstance m, CardInstance grave)
    {
        if (m.IsSilenced) return;
        if (m.Card.Id is "LCG-003" or "LCG-016" or "LCG-025" or "LCG-103" or "LCG-043" or "LCG-108" or "LCG-080" or "LCG-083" or "LCG-093" or "LCG-097")
            Present("trigger", p, m.InstanceId, card: m.Card, label: "離場能力");
        var enemy = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "LCG-003": Damage(enemy, 1); break;
            case "LCG-016": PickLowest(p, enemy.Field, x => KillBatch(new[] { x }, m.Card.Name)); break;
            case "LCG-025": case "LCG-103": DrawMany(p, 1); break;
            case "LCG-043": case "LCG-108": Recover(p, 1, 1, grave); break;
            case "LCG-080": KillBatch(enemy.Field.Where(x => x.CurrentPP <= 1000).ToArray(), m.Card.Name); break;
            case "LCG-083": PickMonster(p, "腐爛食屍鬼離場：選擇 PP 500 以下敵怪", enemy.Field.Where(x => x.CurrentPP <= 500), x => KillBatch(new[] { x }, m.Card.Name)); break;
            case "LCG-093": Revive(p, 2, grave, optional: true); break;
            case "LCG-097": Resolve(() => PickHighest(p, enemy.Field, x => KillBatch(new[] { x }, m.Card.Name)), () => Revive(p, 3, grave)); break;
        }
    }
}
