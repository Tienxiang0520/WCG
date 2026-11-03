using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using WcgWeb.Models;

namespace WcgWeb.Services;

// PvP desync detection (Pvp/PvpLockstep.cs): a stable hash of everything that decides the rest of the match.
// Covered: both seats in full (life, deck order, hand, energy, field, structures, graveyard, every counter and
// per-turn flag on cards and players; cards by catalog id), turn number and owner, phase, revision, opening coins,
// the pending choice/target (owner, prompt, option ids and titles, multi-pick metadata, target kind), the queued
// effect count, planned summon slots, grave-revive turns, the multi-pick sequence and the log text.
// Not covered: log timestamps, PlayerState.IsAi (who drives a seat, not game state), presentation events, AI search caches/plans and the internal state of the seeded
// Random (it is never read here, so hashing does not consume it; any divergence shows up in the next shuffle or draw).
// Instance ids are fresh Guids per engine, so every Guid is renamed to its order of first appearance in the dump:
// two engines that reached the same state by the same actions produce the same text.
public partial class GameEngine
{
    private static readonly JsonSerializerOptions HashJson = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles, WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { HashContract } },
    };
    private static void HashContract(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        for (int i = info.Properties.Count - 1; i >= 0; i--)
        {
            var prop = info.Properties[i];
            if (typeof(Delegate).IsAssignableFrom(prop.PropertyType) || prop.Name is "Time" or "Timestamp" or "IsAi" || prop.Get == null) { info.Properties.RemoveAt(i); continue; }
            if (prop.PropertyType == typeof(CardDefinition))
            {
                var get = prop.Get;
                var name = prop.Name;
                info.Properties.RemoveAt(i);
                var id = info.CreateJsonPropertyInfo(typeof(string), name + "#id");
                id.Get = o => (get(o) as CardDefinition)?.Id;
                info.Properties.Insert(i, id);
            }
        }
    }
    private static readonly Regex GuidPattern = new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);

    // The canonical text the hash is taken over (tests print it to locate a desync).
    internal string StateDump()
    {
        lock (_gate)
        {
            var root = new
            {
                Player, Computer, CurrentTurnPlayerId, TurnNumber, CurrentPhase, Revision, OpeningCoins,
                Choice = CurrentPendingChoice, Target = CurrentPendingTarget, Effects = _effects.Count, Reaction = _reactionDepth,
                Planned = plannedSlots.ToList(), Revive = graveReviveTurn.ToList(), Multi = _multiSeq,
                Log = Logs.Select(l => l.Message + "|" + l.Level).ToList(),
            };
            var json = JsonSerializer.Serialize(root, HashJson);
            var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            return GuidPattern.Replace(json, m => "#" + (ids.TryGetValue(m.Value, out var n) ? n : ids[m.Value] = ids.Count));
        }
    }
    public string StateHash() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(StateDump())))[..32];
    // One seat only (no hidden-information split): used to check that mirrored views agree on each seat.
    internal string SeatHash(PlayerState seat)
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(seat, HashJson);
            var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            json = GuidPattern.Replace(json, m => "#" + (ids.TryGetValue(m.Value, out var n) ? n : ids[m.Value] = ids.Count));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))[..32];
        }
    }
}
