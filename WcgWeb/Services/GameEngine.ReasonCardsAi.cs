using WcgWeb.Models;
namespace WcgWeb.Services;

// AI for the 理智 buff cards. Uses only public facts (the revealed enemy spell or summoned monster, boards)
// and the AI's own hand and deck top, so it never peeks. The choice logic is not version-gated because
// 258–262 and the new 023 prompt only exist in the current catalog; old journals never reach it.
public partial class GameEngine
{
    // v9 (RankedAiVersion 9): the turn search also values keeping enough untapped energy to cast a held
    // 否決之言 / 退潮令 during the opponent's turn. Steps recorded as v8/v7 replay with the earlier rules.
    private bool V9 => AiRevision >= 9;
    private double CounterReserve(PlayerState p)
    {
        var held = p.Hand.Where(c => c.Card.Id is "WCG-259" or "WCG-260").Select(c => ActualCost(p, c.Card)).DefaultIfEmpty(-1).Min();
        return held >= 0 && p.AvailableEnergy >= held ? 1.5 : 0;
    }

    private ChoiceOption? ReasonChoice(PlayerState p, PlayerState e, PendingChoice choice, AiProfile prof)
    {
        ChoiceOption Skip() => choice.Options.First(o => o.Id == "SKIP");
        ChoiceOption Use() => choice.Options.First(o => o.Id != "SKIP");
        switch (choice.Title)
        {
            case NegateTitle:
            {
                if (!prof.UseCounters) return Skip();
                var spell = e.Graveyard.LastOrDefault(c => c.InstanceId == choice.SourceInstanceId)?.Card;
                if (spell == null) return Skip();
                var role = RoleOf(spell);
                bool lethalBurn = role.HasFlag(AiRole.Burn) && CardReach(spell) >= p.Hp;
                bool worth = lethalBurn || role.HasFlag(AiRole.Wipe) || role.HasFlag(AiRole.Removal) && p.Field.Count > 0 || spell.TotalCost >= 3;
                return worth ? Use() : Skip();
            }
            case EbbTideTitle:
            {
                if (!prof.UseCounters) return Skip();
                var m = e.Field.FirstOrDefault(x => x.InstanceId == choice.SourceInstanceId);
                if (m == null) return Skip();
                bool worth = m.Card.TotalCost >= 4 || m.Card.Text.Contains("進場：") && m.Card.TotalCost >= 3 || m.IsTaunt && p.Field.Count > 0;
                return worth ? Use() : Skip();
            }
            case ScryTitle:
            {
                var top = choice.Options[0].PreviewCard;
                if (top == null) return choice.Options[0];
                bool bottom = ActualCost(p, top) > p.TotalEnergy + 2 || top.IsMonster && (top.PP ?? 0) < 900 && TurnNumber > 10;
                return choice.Options.First(o => o.Id == (bottom ? "BOTTOM" : "TOP"));
            }
        }
        return null;
    }
}
