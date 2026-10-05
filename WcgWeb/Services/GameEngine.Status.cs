using WcgWeb.Models;
namespace WcgWeb.Services;

public partial class GameEngine
{
    private PlayerState PlayerById(string? id) => id == Player.Id ? Player : Computer;
    internal bool StackableShields { get; set; } = true;
    private bool CanShield(MonsterInstance m) => Alive(m) && !m.IsSilenced && (StackableShields || !m.HasShield);
    private void Shield(PlayerState p, MonsterInstance m, bool optional = false)
    {
        if (!CanShield(m)) return;
        var energies = p.EnergyZone.Where(e => !e.IsTapped).ToList(); if (energies.Count == 0) return;
        var options = energies.Select((e, i) => new ChoiceOption { Id = e.InstanceId.ToString(), Title = $"背面能量 {i + 1}", Subtitle = "覆蓋期間不能支付費用" }).ToList();
        if (optional) options.Add(new() { Id = "SKIP", Title = "不啟用聖盾" });
        Ask(p, $"為【{m.Card.Name}】選擇覆蓋能量", options, o =>
        {
            if (o.Id == "SKIP" || !CanShield(m)) return;
            var energy = energies.FirstOrDefault(e => e.InstanceId.ToString() == o.Id);
            if (energy == null || energy.IsTapped || !p.EnergyZone.Remove(energy)) return;
            m.ShieldEnergies.Add(energy); m.ShieldOwnerId = p.Id;
            Present("status", Owner(m), m.InstanceId, card: m.Card, label: $"聖盾 ×{m.ShieldCount}");
            Log($"【{m.Card.Name}】獲得聖盾，保留 1 張能量。", "action");
        });
    }
    private void GrantShields(PlayerState p, MonsterInstance source, int count, HashSet<Guid>? chosen = null)
    {
        if (count == 0 || p.AvailableEnergy == 0 || !Alive(source) || source.IsSilenced) return;
        chosen ??= [];
        var targets = p.Field.Where(m => m != source && !chosen.Contains(m.InstanceId) && CanShield(m)).ToList(); if (targets.Count == 0) return;
        Ask(p, "可選擇另一隻己方怪物獲得聖盾", targets.Select(m => new ChoiceOption { Id = m.InstanceId.ToString(), Title = m.Card.Name, PreviewCard = m.Card })
            .Append(new ChoiceOption { Id = "SKIP", Title = "不再授予聖盾" }), o =>
        {
            if (o.Id == "SKIP") return;
            var m = targets.First(x => x.InstanceId.ToString() == o.Id);
            chosen.Add(m.InstanceId);
            Resolve(() => Shield(p, m), () => GrantShields(p, source, count - 1, chosen));
        });
    }
    private void ReleaseShield(MonsterInstance m, bool all = true)
    {
        if (!m.HasShield) return;
        var released = m.ShieldEnergies.Take(all ? m.ShieldCount : 1).ToArray();
        foreach (var energy in released)
        {
            energy.IsTapped = false; PlayerById(m.ShieldOwnerId).EnergyZone.Add(energy);
            m.ShieldEnergies.Remove(energy);
        }
        if (!m.HasShield) m.ShieldOwnerId = null;
        Present("status", Owner(m), m.InstanceId, card: m.Card, label: "聖盾能量返還");
    }
    private void ReleaseAttachments(MonsterInstance m)
    {
        ReleaseShield(m);
        if (m.SilenceSpell is { } spell) PlayerById(m.SilenceOwnerId).Graveyard.Add(spell);
        m.SilenceSpell = null; m.SilenceOwnerId = null;
    }
    private void Freeze(MonsterInstance m)
    {
        if (!Alive(m) || m.IsFrozen) return;
        m.FrozenUntilTurn = TurnNumber + (Owner(m) == ActivePlayer ? 2 : 1);
        Present("status", Owner(m), m.InstanceId, card: m.Card, label: "冰凍");
        Log($"【{m.Card.Name}】冰凍至其下個回合結束。", "action");
    }
    private void Silence(PlayerState p, CardInstance spell, MonsterInstance m)
    {
        if (!Alive(m) || m.IsSilenced) return;
        p.Graveyard.Remove(spell); m.SilenceSpell = spell; m.SilenceOwnerId = p.Id;
        ReleaseShield(m); m.IsSilenced = true; m.IsStealthed = false;
        Present("status", Owner(m), m.InstanceId, card: m.Card, label: "沉默");
        Log($"【{m.Card.Name}】被【{spell.Card.Name}】沉默，法術留在其上。", "action");
    }
}
