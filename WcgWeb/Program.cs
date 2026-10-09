using WcgWeb.Components;
using WcgWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // A cropped avatar (at most 300 KB of text) is sent from the browser in one message.
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 512 * 1024);

builder.Services.AddSingleton<CardDatabase>();
builder.Services.AddScoped<DeckService>();
builder.Services.AddScoped<GameEngine>();
builder.Services.AddScoped<BattleBridge>();
builder.Services.AddScoped<BattleCoordinator>();
builder.Services.AddSingleton<RankedStore>();
builder.Services.AddSingleton<PlayerProfileStore>();
builder.Services.AddSingleton<Localizer>();
builder.Services.AddSingleton<RankedDecks>();
builder.Services.AddSingleton<RankedSession>(sp => new RankedSession(
    sp.GetRequiredService<CardDatabase>(), sp.GetRequiredService<IWebHostEnvironment>(),
    sp.GetRequiredService<RankedStore>(), sp.GetRequiredService<RankedDecks>(), sp.GetRequiredService<ILogger<BattleCoordinator>>()));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
