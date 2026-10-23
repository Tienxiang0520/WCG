using WcgWeb.Models;
namespace WcgWeb.Services;

// 天梯電腦學會新卡（PR E）：磨牌、箭頭站位、附著、手牌反擊、空格、犧牲與墓地降費、傭兵、雙色。
// Journals record the AI version of every step; steps recorded before version 6 replay with the old decisions.
public partial class GameEngine
{
    internal int AiRevision { get; set; } = RankedAiVersion;
    private bool NewAi => AiRevision >= 6;

    // Extra play value for the new cards; null keeps the old scoring.
    private double? NewPlayValue(PlayerState p, CardInstance c)
    {
        if (!NewAi) return null;
        var e = GetOpponent(p); var id = c.Card.Id;
        double Mill(int n) => n <= 0 ? -3 : n >= e.Deck.Count ? 60 : n * (e.Deck.Count <= 12 ? 2.5 : .9);
        double Draw(int n) => p.Deck.Count <= n + 2 ? -10 : n * 2.2;
        double Best(IEnumerable<MonsterInstance> ms) => ms.Select(BodyValue).DefaultIfEmpty(0).Max();
        var others = p.Field.ToArray();
        if (OathPlayValue(p, c) is { } oath) return oath;
        switch (id)
        {
            case "WCG-208" when c.Card.IsSpell:
                // 寂靜禁咒: worth casting only on an enemy whose ability matters (taunt, shield, poison or any text).
                var hush = e.Field.Where(m => !m.IsSilenced && !m.Card.IsVanilla).Select(m => (m.IsTaunt ? 3 : 0) + (m.HasShield ? 2 : 0) + (m.HasPoison ? 2 : 0) + 1 + BodyValue(m) * .3).DefaultIfEmpty(-5).Max();
                return hush - 1;
            case "WCG-211": return Mill(3) - 1;
            case "WCG-214": return Mill(Math.Min(8, p.Graveyard.Count(x => x.Card.IsMonster))) - 2;
            case "WCG-217": return Draw(Math.Min(3, p.Field.Count(HasArrows))) - 1.5;
            case "WCG-220": return p.Field.Any(m => !m.HasTrample && m.CurrentPP >= 1300) ? 3 : -3;
            case "WCG-221": return p.Field.Count == 0 ? -5 : 3 + (p.Field.Any(CanAttack) ? 2 : 0) + (p.Field.Any(m => m.Card.Id == "WCG-222") ? 2 : 0);
            case "WCG-230":
                var duel = p.Field.Where(a => !a.IsTapped).SelectMany(a => e.Field.Where(d => a.CurrentPP > d.CurrentPP && !(d.HasShield && !d.ShieldSpent) && !d.HasPoison).Select(d => BodyValue(d))).DefaultIfEmpty(-6).Max();
                return duel - 1;
            case "WCG-233":
                if (p.Field.Count == 0) return -10;
                var fodder = p.Field.Min(BodyValue);
                return Draw(1) + (p.Field.Any(m => m.Card.HasWill("生機") && BodyValue(m) <= fodder + 1) && p.Hp < 7 ? 1.5 : 0) - fodder * .6 + 1;
            case "WCG-237": return p.Deck.Count > 3 ? 3 : -5;
            case "WCG-242": return p.Hp <= 2 ? -20 : Draw(2) - 1.5;
            case "WCG-248":
                var strongest = p.Field.Where(m => m.Card.HasWill("生機")).Select(ThresholdPP).DefaultIfEmpty(0).Max();
                return Best(e.Field.Where(m => ThresholdPP(m) < strongest)) + 1 - 1.5;
            case "WCG-250": return Draw(p.Graveyard.Count(x => x.Card.IsMonster) >= 5 ? 2 : 1);
        }
        if (!c.Card.IsMonster) return null;
        var bonus = id switch
        {
            "WCG-201" when CardBalanceHistory.CardFixesCatalog(_cardDb) => e.Field.Any(m => m.IsTaunt) ? 2 : 0,
            "WCG-205" or "WCG-209" => e.Field.Any(m => m.HasShield && !m.ShieldSpent) ? 3 : 0,
            "WCG-208" => e.Field.Where(m => !m.IsSilenced).Select(m => (m.IsTaunt ? 2 : 0) + (m.HasShield ? 2 : 0) + (m.HasPoison ? 2 : 0) + (m.Card.IsVanilla ? 0 : 1)).DefaultIfEmpty(0).Max(),
            "WCG-210" or "WCG-261" => e.Field.Any(m => m.IsTaunt) ? 2 : 0,
            "WCG-212" => Mill(2) * .7,
            "WCG-213" => e.Deck.Count <= 12 ? 2 : 0,
            "WCG-219" => p.Occupied >= 4 ? 2 : -1,
            "WCG-222" => -1,
            "WCG-227" => p.Hand.Count - 1 > e.Hand.Count ? 2 : 0,
            "WCG-228" => Math.Max(0, 4 - p.Occupied) * 300 / 450d,
            "WCG-236" => others.All(m => m.Card.IsNeutral) ? 2 : 0,
            "WCG-238" => others.Count(m => m.Card.IsNeutral) * 200 / 450d,
            "WCG-241" => others.All(m => m.Card.HasWill("狂怒")) ? 400 / 450d : 0,
            "WCG-245" => others.All(m => m.Card.HasWill("生機")) ? 2 : 0,
            "WCG-251" => ReasonBuff || others.All(m => m.Card.HasWill("理智")) ? 2 : 0,
            "WCG-252" => p.Hand.Count(x => x.Card.IsSpell && !x.Card.IsCounter) * .5,
            _ => 0d,
        };
        return bonus == 0 ? null : bonus;
    }
    // Slot choice for the new arrow cards: 215/218 want both neighbours, 216 wants the most dangerous enemy ahead.
    private ChoiceOption? NewSlot(PlayerState p, PendingChoice choice)
    {
        if (!NewAi || choice.SourceCard is not { } card || card.Id is not ("WCG-215" or "WCG-216" or "WCG-218")) return null;
        var e = GetOpponent(p);
        double Score(ChoiceOption o)
        {
            var slot = int.Parse(o.Id[5..]);
            if (card.Id == "WCG-216") return e.Field.Where(m => m.Slot == 4 - slot).Select(m => m.CurrentDP * 3 + m.CurrentPP / 500d).DefaultIfEmpty(-1).Max();
            return (p.Board.Any(m => m.Slot == slot - 1) ? 3 : 0) + (p.Board.Any(m => m.Slot == slot + 1) ? 3 : 0) + (slot is > 0 and < 4 ? 1 : 0);
        }
        return choice.Options.Where(o => o.Id.StartsWith("SLOT:", StringComparison.Ordinal)).OrderByDescending(Score).ThenBy(o => o.Id, StringComparer.Ordinal).FirstOrDefault();
    }
    // 230 決鬥宣言: the challenger is already tapped; pick an enemy it beats.
    private MonsterInstance? NewTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal)
    {
        if (!NewAi) return null;
        if (OathTarget(p, pending, legal) is { } oath) return oath;
        if (pending.Title == "選擇與它交戰的敵方怪物")
        {
            var power = p.Field.Where(m => m.IsTapped).Select(m => m.CurrentPP).DefaultIfEmpty(0).Max();
            return legal.Where(m => m.CurrentPP < power && !(m.HasShield && !m.ShieldSpent)).OrderByDescending(BodyValue).FirstOrDefault()
                ?? legal.OrderBy(m => m.CurrentPP).First();
        }
        return null;
    }
}
