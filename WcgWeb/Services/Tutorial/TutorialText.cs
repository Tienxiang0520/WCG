using System.Globalization;

namespace WcgWeb.Services.Tutorial;

public enum TutorialTextField { Name, Cost, PP, DP, Effect, OwnPP, EnemyPP, AvailableEnergy }

// Bind explanations to the same catalogue and board that the lesson actually uses.
public sealed record TutorialTextValue(string CardId, TutorialTextField Field)
{
    public static TutorialTextValue CardName(string id) => new(id, TutorialTextField.Name);
    public static TutorialTextValue CardCost(string id) => new(id, TutorialTextField.Cost);
    public static TutorialTextValue CardPP(string id) => new(id, TutorialTextField.PP);
    public static TutorialTextValue CardDP(string id) => new(id, TutorialTextField.DP);
    public static TutorialTextValue CardEffect(string id) => new(id, TutorialTextField.Effect);
    public static TutorialTextValue OwnPP(string id) => new(id, TutorialTextField.OwnPP);
    public static TutorialTextValue EnemyPP(string id) => new(id, TutorialTextField.EnemyPP);
    public static TutorialTextValue AvailableEnergy() => new("", TutorialTextField.AvailableEnergy);

    public object Resolve(CardDatabase cards, GameEngine engine, Localizer? language = null)
    {
        if (Field == TutorialTextField.AvailableEnergy) return engine.Player.AvailableEnergy;
        var card = cards.GetCard(CardId) ?? throw new InvalidOperationException($"Unknown tutorial card: {CardId}");
        return Field switch
        {
            TutorialTextField.Name => language?.CardName(card) ?? card.Name,
            TutorialTextField.Cost => card.TotalCost,
            TutorialTextField.PP => card.PP ?? throw new InvalidOperationException($"Tutorial card has no PP: {CardId}"),
            TutorialTextField.DP => card.DP ?? throw new InvalidOperationException($"Tutorial card has no DP: {CardId}"),
            TutorialTextField.Effect => language?.CardText(card) ?? card.Text,
            TutorialTextField.OwnPP => engine.Player.Field.FirstOrDefault(m => m.Card.Id == CardId)?.CurrentPP ?? card.PP
                ?? throw new InvalidOperationException($"Tutorial card has no PP: {CardId}"),
            TutorialTextField.EnemyPP => engine.Computer.Field.FirstOrDefault(m => m.Card.Id == CardId)?.CurrentPP ?? card.PP
                ?? throw new InvalidOperationException($"Tutorial card has no PP: {CardId}"),
            _ => throw new InvalidOperationException($"Unknown tutorial field: {Field}")
        };
    }
}

public static class TutorialText
{
    public static string Render(string format, TutorialTextValue[] values, CardDatabase cards, GameEngine engine, Localizer? language = null)
        => engine.ReadConsistent(() =>
        {
            var args = values.Select(value => value.Resolve(cards, engine, language)).ToArray();
            return language != null ? language.F(format, args) : string.Format(CultureInfo.InvariantCulture, format, args);
        });
}
