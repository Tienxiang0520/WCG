using WcgWeb.Models;
namespace WcgWeb.Services;

// AI v7 simulation support. A clone copies a quiescent state (no pending choice, empty effect queue)
// onto a private engine that never animates, logs or touches the real match's RNG.
// Hidden information is removed for the viewer: the opponent's hand, deck and face-down cards become
// placeholder cards, and the viewer's own deck is reshuffled, so a search can never learn hidden cards.
public partial class GameEngine
{
    private bool _simulation;
    internal bool IsSimulation => _simulation;
    internal static readonly CardDefinition HiddenCard = new() { Id = "SIM-HIDDEN", Name = "未知", Type = "法術", TotalCost = 99, Text = "" };
    internal bool IsQuiescent => !IsWaiting && _effects.Count == 0 && _reactionDepth == 0;

    internal GameEngine CloneQuiescent(int simSeed, string viewerId)
    {
        lock (_gate)
        {
            if (!IsQuiescent) throw new InvalidOperationException("只能在效果結算完成時複製對局。");
            var rng = new Random(simSeed);
            var g = new GameEngine(_cardDb, new Random(rng.Next())) { _simulation = true };
            var map = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            Guid NewId() { var b = new byte[16]; rng.NextBytes(b); return new Guid(b); }
            CardInstance Card(CardInstance c, bool hidden)
            {
                if (map.TryGetValue(c, out var done)) return (CardInstance)done;
                var copy = new CardInstance(hidden ? HiddenCard : c.Card) { InstanceId = hidden ? NewId() : c.InstanceId, IsTapped = c.IsTapped, TemporaryEnergy = c.TemporaryEnergy };
                map[c] = copy; return copy;
            }
            MonsterInstance Unit(MonsterInstance m, bool hidden)
            {
                if (map.TryGetValue(m, out var done)) return (MonsterInstance)done;
                var copy = new MonsterInstance(hidden ? HiddenCard : m.Card)
                {
                    InstanceId = hidden ? NewId() : m.InstanceId, Slot = m.Slot, IsTapped = m.IsTapped, IsSet = m.IsSet,
                    ShieldQualified = m.ShieldQualified, ShieldSpent = m.ShieldSpent, ShieldCounters = m.ShieldCounters, ShieldSources = [.. m.ShieldSources], WasSilenced = m.WasSilenced, NextCombatBonus = m.NextCombatBonus, TurnBonus = m.TurnBonus,
                    TriggersThisTurn = m.TriggersThisTurn, CombatPowerBonus = m.CombatPowerBonus, HasAttacked = m.HasAttacked, AttackBanned = m.AttackBanned, FromEnergy = m.FromEnergy,
                    HasSummoningSickness = m.HasSummoningSickness, IsSilenced = m.IsSilenced, IsStealthed = m.IsStealthed,
                    ExtraTaunt = m.ExtraTaunt, ExtraTrample = m.ExtraTrample, ShieldOwnerId = m.ShieldOwnerId, SilenceOwnerId = m.SilenceOwnerId,
                };
                copy.CurrentPP = m.BasePP;
                copy.CurrentDP = m.BaseDP; copy.AttackDamageBonus = m.AttackDamageBonus; copy.DpCounters = m.DpCounters; copy.DpBonus = m.DpBonus; copy.TurnDpBonus = m.TurnDpBonus; copy.TurnUnblockable = m.TurnUnblockable;
                copy.OwnerId = m.OwnerId; copy.Turned = m.Turned; copy.ControlUntil = m.ControlUntil; copy.SoulCounter = m.SoulCounter;
                map[m] = copy;
                copy.Attachments = m.Attachments.Select(a => a with { Card = Card(a.Card, false) }).ToList();
                copy.LostAttachments = m.LostAttachments.Select(c => Card(c, false)).ToList();
                copy.ShieldEnergies = m.ShieldEnergies.Select(c => Card(c, false)).ToList();
                copy.SilenceSpell = m.SilenceSpell == null ? null : Card(m.SilenceSpell, false);
                return copy;
            }
            var hideEnergy = _cardDb.GetCard("WCG-301") != null;
            PlayerState Side(PlayerState s)
            {
                bool viewer = s.Id == viewerId;
                var copy = new PlayerState
                {
                    Id = s.Id, Name = s.Name, IsAi = true, Hp = s.Hp, NextCreatureDiscount = s.NextCreatureDiscount,
                    HasLost = s.HasLost, LossReason = s.LossReason, HasFilledEnergyThisTurn = s.HasFilledEnergyThisTurn, EnergyAmbushTurn = s.EnergyAmbushTurn, Lent = s.Lent, LaneRushTurn = s.LaneRushTurn, BurnedEnergy = new(s.BurnedEnergy),
                };
                copy.Hand = s.Hand.Select(c => Card(c, !viewer)).ToList();
                copy.Deck = s.Deck.Select(c => Card(c, !viewer)).ToList();
                if (viewer) for (int i = copy.Deck.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (copy.Deck[i], copy.Deck[j]) = (copy.Deck[j], copy.Deck[i]); }
                // From the 伏能喚醒 catalog on, energy cards matter (they can be summoned), so the opponent's face-down energy is hidden too.
                copy.EnergyZone = s.EnergyZone.Select(c => Card(c, hideEnergy && !viewer && !c.TemporaryEnergy)).ToList();
                copy.Graveyard = s.Graveyard.Select(c => Card(c, false)).ToList();
                copy.Field = s.Field.Select(m => Unit(m, false)).ToList();
                copy.Structures = s.Structures.Select(m => Unit(m, m.IsSet && !viewer)).ToList();
                return copy;
            }
            g.Player = Side(Player); g.Computer = Side(Computer);
            g.CurrentTurnPlayerId = CurrentTurnPlayerId; g.TurnNumber = TurnNumber; g.CurrentPhase = CurrentPhase;
            g.MatchId = MatchId; g.Revision = Revision;
            g.LimitFactionTriggers = LimitFactionTriggers; g.LegacyCardRules = LegacyCardRules; g.StackableShields = StackableShields;
            g.TrainingMode = TrainingMode; g.AiLevel = AiLevel; g.AiRevision = AiRevision; g._aiSeed = _aiSeed;
            foreach (var (k, v) in plannedSlots) g.plannedSlots[k] = v;
            g.soulPactForeign = soulPactForeign;
            g.lastSacrifice = lastSacrifice != null && map.TryGetValue(lastSacrifice, out var ls) ? (MonsterInstance)ls : null;
            g.RefreshBoard();
            return g;
        }
    }

    // Runs one action on a simulation, then answers every choice it raises with the v6 choice logic
    // until the state is quiescent again. Returns false when the action was illegal or did not settle.
    internal bool SimApply(Func<GameEngine, bool> action, int choiceLevel = 5)
    {
        if (!_simulation) throw new InvalidOperationException("SimApply 只能用在模擬對局。");
        try
        {
            if (!action(this)) return false;
            for (int i = 0; i < 40 && IsWaiting && !IsOver; i++)
            {
                int level = AiLevel, revision = AiRevision; AiLevel = choiceLevel; AiRevision = 6;
                bool ok;
                try { ok = ExecuteAiStep(); } finally { AiLevel = level; AiRevision = revision; }
                if (!ok) return false;
            }
            return IsOver || IsQuiescent;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or ArgumentException or IndexOutOfRangeException or FormatException)
        {
            return false;
        }
    }
}
