using WcgWeb.Models;
using WcgWeb.Models.Battle;

namespace WcgWeb.Services;

public partial class GameEngine
{
    private readonly List<BattleEvent> _presentation = new();
    internal string RevealTitle { get; private set; } = "";
    internal BattleCard[] RevealedCards { get; private set; } = [];
    // Snapshots and command batches share the mutation lock. Never return engine objects.
    internal T ReadConsistent<T>(Func<T> read) { lock (_gate) return read(); }
    internal BattleEvent[] PresentationEvents => _presentation.ToArray();
    internal static BattleCard VisibleCard(CardInstance c) => VisibleCard(c.Card, c.InstanceId);
    internal static BattleCard VisibleCard(CardDefinition c, Guid id) =>
        new(id, c.Id, c.Name, c.Type, c.Will, c.TotalCost, c.PP, c.DP, c.Text, "", c.Arrows.ToArray());
    private void Present(string type, PlayerState side, Guid? instance = null, Guid? target = null,
        int amount = 0, CardDefinition? card = null, string label = "") =>
        _presentation.Add(new(Guid.NewGuid(), Revision + 1, _presentation.Count, type, side.Id,
            instance, target, amount, card == null ? null : VisibleCard(card, instance ?? Guid.Empty), label));
}
