using System.Security.Cryptography;

namespace WcgCloud;

// SHA-256 of the exact data files this API validates saves against. The website's asset manifest lists the
// same files (data/cards.json, data/preset_decks.json, data/ranked_decks.json) with their SHA-256, so the
// frontend and tools/deploy-all.py can tell when the site and the API were built from different catalogs.
public static class CloudCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> hashes = new(() => new Dictionary<string, string>
    {
        ["cards"] = Hash("WcgCloud.Cards"), ["presets"] = Hash("WcgCloud.Presets"), ["rankedDecks"] = Hash("WcgCloud.RankedDecks")
    });
    public static IReadOnlyDictionary<string, string> Hashes => hashes.Value;
    private static string Hash(string resource)
    {
        using var stream = typeof(CloudCatalog).Assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException("Missing catalog resource.");
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
