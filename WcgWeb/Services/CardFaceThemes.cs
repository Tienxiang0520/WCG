namespace WcgWeb.Components.Common;

// Per-will ornaments for the card face. Order keeps the physical card's four-point stars and compass;
// every other will has its own corner ornament, name spark, footer gem and effect-box emblem.
// battle-feedback.js (spell showcase) carries the same shapes in FACE_THEMES; CardFaceTests keeps them in sync.
public static class CardFaceThemes
{
    public sealed record Theme(string Corner, string Spark, string Gem, string Emblem);
    public const string Star = "M10 0 11.6 8.4 20 10 11.6 11.6 10 20 8.4 11.6 0 10 8.4 8.4Z";
    private const string Compass = "<circle cx=\"50\" cy=\"50\" r=\"44\"/><circle cx=\"50\" cy=\"50\" r=\"31\"/><circle cx=\"50\" cy=\"50\" r=\"6\"/><path d=\"M50 2 56 44 98 50 56 56 50 98 44 56 2 50 44 44Z\"/><path d=\"M50 18 53 47 82 50 53 53 50 82 47 53 18 50 47 47Z\" transform=\"rotate(45 50 50)\"/>";
    public static readonly IReadOnlyDictionary<string, Theme> All = new Dictionary<string, Theme>
    {
        ["order"] = new(Star, Star, Star, Compass),
        // Fury: cracked lava shards and flames.
        ["wrath"] = new("M1 1 19 3 13 6 17 9 9 9 11 13 6 12 3 19Z", "M10 0C13 5 17 8 16 13 15 17 12 20 10 20 8 20 5 17 4 13 4 10 7 8 8 4 10 8 11 10 10 0Z", "M10 1 15 8 12 19 8 19 5 8Z",
            "<circle cx=\"50\" cy=\"50\" r=\"45\"/><path d=\"M50 6C62 26 78 34 74 62 72 80 60 92 50 94 40 92 28 80 26 62 24 46 36 40 40 24 46 36 44 46 50 52 58 40 56 22 50 6Z\"/><path d=\"M8 30 30 40 22 52 40 58M92 70 70 60 78 48 60 42\"/>"),
        // Reason: silver filigree brackets, runic hexagons and a hexagram.
        ["reason"] = new("M1 1H14V3H3V14H1ZM5 5H11V6.6H6.6V11H5ZM9 9H12V12H9Z", "M10 1 18 5.5V14.5L10 19 2 14.5V5.5ZM10 5 5.5 7.5V12.5L10 15 14.5 12.5V7.5Z", "M10 2 18 10 10 18 2 10ZM10 6 6 10 10 14 14 10Z",
            "<circle cx=\"50\" cy=\"50\" r=\"45\"/><circle cx=\"50\" cy=\"50\" r=\"36\"/><path d=\"M50 14 81 68H19ZM50 86 19 32H81Z\"/><circle cx=\"50\" cy=\"50\" r=\"10\"/><path d=\"M50 5V14M50 86V95M5 50H14M86 50H95\"/>"),
        // Life: living bark curls and leaves.
        ["vitality"] = new("M1 19C1 9 5 3 13 1 10 4 9 7 9 10 12 7 16 6 19 7 15 9 12 12 11 16 8 14 5 15 1 19Z", "M3 17C3 8 9 3 18 2 18 11 12 17 3 17ZM5 15 15 5 14.4 4.4 4.4 14.4Z", "M10 1C16 5 17 12 10 19 3 12 4 5 10 1Z",
            "<circle cx=\"50\" cy=\"50\" r=\"45\"/><path d=\"M50 8C78 26 84 60 50 92 16 60 22 26 50 8Z\"/><path d=\"M50 14V88M50 40 68 28M50 54 72 42M50 68 66 58M50 40 32 28M50 54 28 42M50 68 34 58\"/>"),
        // Abyss: thorns, a watching eye and tentacles.
        ["abyss"] = new("M1 1 19 5 9 6 12 9 7 8 6 12 5 7 4 19Z", "M1 10Q10 1 19 10 10 19 1 10ZM10 6.5Q12.2 10 10 13.5 7.8 10 10 6.5Z", "M10 1 13 8 19 10 13 12 10 19 7 12 1 10 7 8Z",
            "<path d=\"M8 50Q50 12 92 50 50 88 8 50Z\"/><circle cx=\"50\" cy=\"50\" r=\"15\"/><path d=\"M50 37Q57 50 50 63 43 50 50 37Z\"/><path d=\"M22 68Q12 84 28 94M78 68Q88 84 72 94M50 78Q44 90 54 98M30 30Q18 18 24 6M70 30Q82 18 76 6\"/>"),
        // Neutral: plain riveted grey stone.
        ["neutral"] = new("M10 3A7 7 0 1 1 9.99 3ZM10 6A4 4 0 1 0 10.01 6Z", "M10 3A7 7 0 1 1 9.99 3Z", "M10 4A6 6 0 1 1 9.99 4Z",
            "<circle cx=\"50\" cy=\"50\" r=\"44\"/><circle cx=\"50\" cy=\"50\" r=\"30\"/><path d=\"M50 22 78 50 50 78 22 50Z\"/><circle cx=\"50\" cy=\"8\" r=\"3\"/><circle cx=\"50\" cy=\"92\" r=\"3\"/><circle cx=\"8\" cy=\"50\" r=\"3\"/><circle cx=\"92\" cy=\"50\" r=\"3\"/>"),
    };
    public static string Key(string? will) => will switch { "狂怒" => "wrath", "理智" => "reason", "生機" => "vitality", "秩序" => "order", "深淵" => "abyss", _ => "neutral" };
    public static Theme For(string key) => All.TryGetValue(key, out var t) ? t : All["neutral"];
}
