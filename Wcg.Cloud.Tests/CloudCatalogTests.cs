using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WcgCloud;

namespace WcgCloudTests;
public sealed class CloudCatalogTests
{
    // The website compares these with the SHA-256 of its own data files (asset manifest); both are built from WcgWeb/Data.
    [Theory]
    [InlineData("cards", "cards.json")] [InlineData("presets", "preset_decks.json")] [InlineData("rankedDecks", "ranked_decks.json")]
    public void HealthReportsTheHashOfTheExactDataFiles(string key, string file)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "WCG.slnx"))) root = Path.GetDirectoryName(root)!;
        var expected = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "WcgWeb", "Data", file))));
        Assert.Equal(expected, CloudCatalog.Hashes[key]);
    }
    [Fact]
    public void HealthIsUncachedAndIncludesTheCatalog()
    {
        var context = new DefaultHttpContext();
        var result = Assert.IsType<OkObjectResult>(new CloudFunctions(null!, null!, null!).Health(context.Request));
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
        Assert.Contains(CloudCatalog.Hashes["cards"], json);
    }
}
