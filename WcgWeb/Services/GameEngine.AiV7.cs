using WcgWeb.Models;
namespace WcgWeb.Services;

// Ranked AI v7 (ai-design.md). Journals record RulesVersion; steps recorded with version 6 or earlier replay
// with ExecuteTieredAi, so v6 stays untouched. v7 is deterministic: budgets count simulated nodes, never time.
public partial class GameEngine
{
    private bool ExecuteV7Ai() => ExecuteTieredAi();
}
