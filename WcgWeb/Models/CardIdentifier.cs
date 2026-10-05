namespace WcgWeb.Models;

public static class CardIdentifier
{
    // Read prior saved decks without rewriting player files during an upgrade.
    public const string LegacyPrefix = "LCG-";
    public const string CurrentPrefix = "WCG-";

    public static string Canonical(string id) => id != null && id.StartsWith(LegacyPrefix, StringComparison.Ordinal)
        ? CurrentPrefix + id[LegacyPrefix.Length..] : id!;
}
