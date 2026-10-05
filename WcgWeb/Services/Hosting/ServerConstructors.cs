using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
namespace WcgWeb.Services;

public partial class CardDatabase
{
    public CardDatabase(IWebHostEnvironment env) : this(
        File.ReadAllText(System.IO.Path.Combine(env.ContentRootPath, "Data", "cards.json")),
        File.ReadAllText(System.IO.Path.Combine(env.ContentRootPath, "Data", "preset_decks.json"))) { }
}
public partial class DeckService
{
    public DeckService(CardDatabase cards, IWebHostEnvironment env) : this(cards,
        new FilePlayerStorage(new Dictionary<string, string> { ["decks"] = System.IO.Path.Combine(env.ContentRootPath, "Data", "custom_decks.json") })) { }
}
public sealed partial class RankedDecks
{
    public RankedDecks(CardDatabase cards, IWebHostEnvironment env) : this(cards,
        File.ReadAllText(System.IO.Path.Combine(env.ContentRootPath, "Data", "ranked_decks.json"))) { }
}
public sealed partial class RankedStore
{
    public RankedStore(IConfiguration configuration) : this(SavePath(configuration)) { }
    private RankedStore(string path) : this(new FilePlayerStorage(new Dictionary<string, string> { ["ranked"] = path }), path) { }
    private static string SavePath(IConfiguration configuration) => System.IO.Path.GetFullPath(configuration["Ranked:SavePath"] ??
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulAndWill", "ranked.json"));
}
public sealed partial class RankedSession
{
    public RankedSession(CardDatabase cards, IWebHostEnvironment env, RankedStore store, RankedDecks opponents,
        ILogger<BattleCoordinator> logger, TimeProvider? clock = null) : this(cards, new DeckService(cards, env), store, opponents, logger, clock) { }
}
