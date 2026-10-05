using LcgWeb.Components;
using LcgWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<CardDatabase>();
builder.Services.AddScoped<DeckService>();
builder.Services.AddScoped<GameEngine>();
builder.Services.AddScoped<BattleBridge>();
builder.Services.AddScoped<BattleCoordinator>();
builder.Services.AddSingleton<RankedStore>();
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
