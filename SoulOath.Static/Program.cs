using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using LcgWeb;
using LcgWeb.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
var client = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
// Only public, read-only game content is loaded from the host.
var database = new CardDatabase(await client.GetStringAsync("data/cards.json"), await client.GetStringAsync("data/preset_decks.json"));
var rankedDecks = new RankedDecks(database, await client.GetStringAsync("data/ranked_decks.json"));
builder.Services.AddSingleton(database);
builder.Services.AddSingleton(rankedDecks);
builder.Services.AddSingleton<IPlayerStorage, BrowserPlayerStorage>();
builder.Services.AddSingleton<DeckService>();
builder.Services.AddSingleton<GameEngine>();
builder.Services.AddSingleton<BattleBridge>();
builder.Services.AddSingleton<BattleCoordinator>();
builder.Services.AddSingleton(sp => new RankedStore(sp.GetRequiredService<IPlayerStorage>()));
builder.Services.AddSingleton(sp => new RankedSession(database, sp.GetRequiredService<DeckService>(),
    sp.GetRequiredService<RankedStore>(), rankedDecks, sp.GetRequiredService<ILogger<BattleCoordinator>>()));
await builder.Build().RunAsync();
