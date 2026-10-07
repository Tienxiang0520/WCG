using WcgWeb.Models;
namespace WcgWeb.Services;

public partial class GameEngine
{
    private void Deploy(PlayerState p, MonsterInstance m)
    {
        if (!Alive(m) || m.IsSilenced) return;
        var enemy = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "WCG-001": case "WCG-102":
                var modes = new List<ChoiceOption> { new() { Id = "LOOT", Title = "抽 1 張，然後棄 1 張" } };
                if (Targetable(p, enemy.Field.Where(x => x.CurrentPP <= 500)).Count > 0)
                    modes.Insert(0, new() { Id = "KILL", Title = "消滅 1 隻 PP 500 以下敵怪" });
                Ask(p, $"【{m.Card.Name}】進場抉擇", modes, o =>
                { if (o.Id == "LOOT") Loot(p); else PickMonster(p, "選擇 PP 500 以下敵怪", enemy.Field.Where(x => x.CurrentPP <= 500), x => KillBatch(new[] { x }, m.Card.Name)); });
                break;
            case "WCG-007": PickLowest(p, enemy.Field.Where(x => x.CurrentPP <= 500), x => KillBatch(new[] { x }, m.Card.Name)); break;
            case "WCG-013": Resolve(() => Damage(enemy, 1), () => Damage(p, 1)); break;
            case "WCG-015": if (enemy.Field.Count > p.Field.Count) DrawMany(p, 1); break;
            case "WCG-020": KillBatch(enemy.Field.Where(x => x.CurrentPP <= 1300).ToArray(), m.Card.Name); break;
            case "WCG-021": case "WCG-116": DrawMany(p, 1); break;
            case "WCG-027": case "WCG-047": case "WCG-115": FreeHand(p, 1, 1); break;
            case "WCG-067": case "WCG-104": PickMonster(p, "選擇費用 2 以下敵怪回手", enemy.Field.Where(x => x.Card.TotalCost <= 2), Bounce); break;
            case "WCG-033": if (enemy.Hand.Count > p.Hand.Count) DrawMany(p, 1); break;
            case "WCG-035": var tapped=enemy.Field.Where(x=>x.IsTapped).ToArray(); foreach(var x in enemy.Field.Where(x=>!x.IsTapped)) x.IsTapped=true; OptionalTarget(p,"可選原本橫置敵怪回手",tapped,Bounce); break;
            case "WCG-039": DrawMany(p, 2); break;
            case "WCG-040":
                if (p == Player) { RevealedCards = enemy.Hand.Select(VisibleCard).ToArray(); RevealTitle = "檢視時的對手手牌"; }
                Log($"【{m.Card.Name}】檢視對手手牌：{string.Join("、", enemy.Hand.Select(c => c.Card.Name))}");
                CardChoice(p, "選擇對手手牌中的法術棄掉", enemy.Hand.Where(c => c.Card.IsSpell), c => { enemy.Hand.Remove(c); enemy.Graveyard.Add(c); }); break;
            case "WCG-041":
                Ask(p, "利爪巨鹿進場抉擇", new[] { new ChoiceOption { Id = "HEAL", Title = "回復 1 點生命" }, new ChoiceOption { Id = "LOOT", Title = "抽 1 張，棄 1 張" } },
                    o => { if (o.Id == "HEAL") Heal(p, 1); else Loot(p); }); break;
            case "WCG-045": if (p.Field.Any(x => x != m)) Loot(p); break;
            case "WCG-061": break;
            case "WCG-073": break;
            case "WCG-057": case "WCG-075": case "WCG-106": Heal(p, 1); break;
            case "WCG-077": PickMonster(p, "選擇 PP 2300 以上敵怪", enemy.Field.Where(x => x.CurrentPP >= 2300), x => KillBatch(new[] { x }, m.Card.Name)); break;
            case "WCG-085": Damage(p, 1); break;
            case "WCG-087": if (p.Graveyard.Count >= 5) Heal(p, 1); break;
            case "WCG-091": KillBatch(enemy.Field.Where(x => x.CurrentPP <= 500).ToArray(), m.Card.Name); break;
            case "WCG-100": Resolve(() => Heal(p, 2), () => Recover(p, int.MaxValue, 1)); break;
            case "WCG-105": KillBatch(p.Field.Concat(enemy.Field).Where(x => x != m && x.CurrentPP <= 500).ToArray(), m.Card.Name); break;
            case "WCG-112": Dispel(p); break;
            case "WCG-117": PickLowest(p, enemy.Field.Where(x => x.CurrentPP <= 1000), x => KillBatch(new[] { x }, m.Card.Name)); break;
            default: ExtraDeploy(p,m); break;
            // Remaining monsters are printed keywords, plain stats, continuous/event or death abilities.
        }
    }
    private void FreeHand(PlayerState p, int maxCost, int count)
    {
        if (count <= 0 || p.Occupied >= 5 || IsOver) return;
        CardChoice(p, $"可從手牌免費召喚費用 {maxCost} 以下怪物", p.Hand.Where(c => c.Card.IsMonster && c.Card.TotalCost <= maxCost), c =>
        {
            if (!p.Hand.Remove(c) || p.Occupied >= 5) return;
            ChooseSlot(p,c.Card,slot=>{plannedSlots[c.InstanceId]=slot;Summon(p,c,false);FreeHand(p,maxCost,count-1);});
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
        if (p.Occupied >= 5) return;
        CardChoice(p, "選擇墓地怪物非付費召喚", p.Graveyard.Where(c => c != exclude && c.Card.IsMonster && c.Card.TotalCost <= maxCost), c =>
        { if(p.Occupied<5)ChooseSlot(p,c.Card,slot=>{if(p.Graveyard.Remove(c)){plannedSlots[c.InstanceId]=slot;Summon(p,c,false);}}); }, optional);
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
            case "WCG-004": case "WCG-048": Resolve(KillTarget, () => DrawMany(p, 1)); break;
            case "WCG-006": SpellDamage(p,enemy, 2); break;
            case "WCG-014": SpellDamage(p,enemy, 1); break;
            case "WCG-046": if (mode == "KILL") KillTarget(); else Loot(p); break;
            case "WCG-054": Search(p, 5, x => x.Card.IsMonster, 2); break;
            case "WCG-062": case "WCG-074":
                Resolve(() => { if (target != null && CanShield(target)) Attach(p, source, target); },
                    () => { if (c.Id == "WCG-062") Heal(p, 1); else DrawMany(p, 1); }); break;
            case "WCG-066": Resolve(() => Heal(p, 2), () => DrawMany(p, 1)); break;
            case "WCG-070": Heal(p, 1); break;
            case "WCG-072": Resolve(() => { if (target != null && SpellTargets(p, c).Contains(target)) Silence(p, source, target); }, () => DrawMany(p, 1)); break;
            case "WCG-002": Resolve(KillTarget, () => { if (p.Hp <= 3) SpellDamage(p,enemy, 1); }); break;
            case "WCG-008": KillBatch(p.Field.Concat(enemy.Field).Where(m => m.CurrentPP <= 500).ToArray(), c.Name); break;
            case "WCG-010": DrawMany(p, Math.Min(2, p.Field.Count)); break;
            case "WCG-064": case "WCG-012": case "WCG-032": case "WCG-076": case "WCG-082": case "WCG-084": case "WCG-088": KillTarget(); break;
            case "WCG-017": case "WCG-123": Resolve(KillTarget, () => SpellDamage(p,enemy, 1)); break;
            case "WCG-019": Resolve(() => Damage(p, 2), () => DrawMany(p, 2)); break;
            case "WCG-022": Search(p, 3, x => x.Card.IsSpell, optional: true); break;
            case "WCG-024": if(target!=null&&Alive(target)){if(target.IsTapped)Loot(p);else target.IsTapped=true;} break;
            case "WCG-026": if (target != null && SpellTargets(p, c).Contains(target)) Bounce(target); break;
            case "WCG-028": case "WCG-096": DrawMany(p, 2); break;
            case "WCG-030": Search(p, 3, _ => true); break;
            case "WCG-034": DrawMany(p, 3); break;
            case "WCG-036": Resolve(() => { if (target != null && SpellTargets(p, c).Contains(target)) Bounce(target); }, () => DrawMany(p, 1)); break;
            case "WCG-038": foreach (var m in p.Field.Concat(enemy.Field).Where(m => m.Card.TotalCost <= 2).ToArray()) Bounce(m); break;
            case "WCG-042":
                var options = new List<ChoiceOption> { new() { Id = "DRAW", Title = "抽 1 張牌" } };
                if (p.Occupied < 5 && p.Hand.Any(x => x.Card.IsMonster && x.Card.TotalCost <= 1)) options.Add(new() { Id = "SUMMON", Title = "從手牌免費召喚 1 費以下怪物" });
                Ask(p, "野性之力抉擇", options, o => { if (o.Id == "DRAW") DrawMany(p, 1); else FreeHand(p, 1, 1); }); break;
            case "WCG-044":
                if (p.Deck.Count > 0) { var energy = p.Deck[0]; p.Deck.RemoveAt(0); energy.IsTapped = true; p.EnergyZone.Add(energy); Present("energy", p, energy.InstanceId, label: "額外背面能量"); Log("滋養萌發：額外能量背面橫置進場。"); } break;
            case "WCG-050": Heal(p, 2); break;
            case "WCG-052": FreeHand(p, 2, 2); break;
            case "WCG-056": Search(p, 3, x => x.Card.IsMonster && x.Card.TotalCost <= 2); break;
            case "WCG-058": Recover(p, 2, 2, optional: true); break;
            case "WCG-068": KillBatch(enemy.Field.Where(m => m.CurrentPP <= 700).ToArray(), c.Name); break;
            case "WCG-078": Resolve(() => KillBatch(enemy.Field.Where(m => m.CurrentPP <= 700).ToArray(), c.Name), () => Heal(p, 1)); break;
            case "WCG-086": Resolve(KillTarget, () => DrawMany(p, 1)); break;
            case "WCG-090": Recover(p, int.MaxValue, 1); break;
            case "WCG-092": KillBatch(p.Field.Concat(enemy.Field).Where(m => m.CurrentPP <= 800).ToArray(), c.Name); break;
            case "WCG-094": Resolve(KillTarget, () => Heal(p, 1)); break;
            case "WCG-098": KillBatch(p.Field.Concat(enemy.Field).ToArray(), c.Name); break;
            case "WCG-121": DrawMany(p, 1); break;
            case "WCG-122": Search(p, 5, x => x.Card.IsMonster); break;
            default: ExtraSpell(p,source,target); break;
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
                if (listener.Card.Id == "WCG-011" && ReferenceEquals(listenerOwner, owner)) triggers.Add(() => { Present("trigger", listenerOwner, listener.InstanceId, card: listener.Card, label: "死亡觸發"); DrawMany(listenerOwner, 1); });
                if (listener.Card.Id == "WCG-148" && ReferenceEquals(listenerOwner,owner)) triggers.Add(()=>TapEnemyEnergy(GetOpponent(owner)));
                if (listener.Card.Id == "WCG-099") triggers.Add(() => { Present("trigger", listenerOwner, listener.InstanceId, card: listener.Card, label: "死亡觸發"); Heal(listenerOwner, 1); });
            }
        }
        Resolve(triggers.ToArray());
    }
    private void Death(PlayerState p, MonsterInstance m, CardInstance grave)
    {
        if (m.IsSilenced) return;
        if (m.Card.Id is "WCG-003" or "WCG-016" or "WCG-025" or "WCG-103" or "WCG-043" or "WCG-108" or "WCG-080" or "WCG-083" or "WCG-093" or "WCG-097")
            Present("trigger", p, m.InstanceId, card: m.Card, label: "離場能力");
        var enemy = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "WCG-003": Damage(enemy, 1); break;
            case "WCG-016": PickLowest(p, enemy.Field, x => KillBatch(new[] { x }, m.Card.Name)); break;
            case "WCG-025": case "WCG-103": DrawMany(p, 1); break;
            case "WCG-043": case "WCG-108": Recover(p, 1, 1, grave); break;
            case "WCG-080": KillBatch(enemy.Field.Where(x => x.CurrentPP <= 1000).ToArray(), m.Card.Name); break;
            case "WCG-083": PickMonster(p, "腐爛食屍鬼離場：選擇 PP 500 以下敵怪", enemy.Field.Where(x => x.CurrentPP <= 500), x => KillBatch(new[] { x }, m.Card.Name)); break;
            case "WCG-093": Revive(p, 2, grave, optional: true); break;
            case "WCG-131":FreeHand(p,2,1);break;
            case "WCG-150":KillBatch(p.Field.Concat(enemy.Field).Where(x=>x.CurrentPP<=700).ToArray(),m.Card.Name);break;
            case "WCG-172":TopEnergy(p);break;
            case "WCG-097": Resolve(() => PickHighest(p, enemy.Field, x => KillBatch(new[] { x }, m.Card.Name)), () => Revive(p, 3, grave)); break;
        }
    }
}
