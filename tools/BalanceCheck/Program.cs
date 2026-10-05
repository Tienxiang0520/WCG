using System.Text.Json;
using WcgWeb.Services;
using Microsoft.AspNetCore.Builder;
var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("WcgWeb");
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root });
var db = new CardDatabase(builder.Environment);
int seeds = args.Length > 1 ? int.Parse(args[1]) : 30;
var records = new List<object>();
for (int i = 0; i < db.PresetDecks.Count; i++) for (int j = i; j < db.PresetDecks.Count; j++)
{
    int wins = 0, losses = 0, firstWins = 0, stepsTotal = 0, turnTotal = 0, fatigue = 0;
    for (int seed = 0; seed < seeds; seed++) foreach (bool first in new[] { true, false })
    {
        var e = new GameEngine(db, new Random(seed + 100)); e.StartGame(db.PresetDecks[i], db.PresetDecks[j], first); e.Player.IsAi = true;
        int step = 0;
        for (; step < 4000 && !e.IsOver; step++) if (!e.ExecuteAiStep()) throw new InvalidOperationException($"Stalled {i}/{j}/{seed}: {e.LastError}");
        if (!e.IsOver) throw new InvalidOperationException("Simulation exceeded action budget.");
        if (e.Computer.HasLost) wins++; else losses++;
        if (first == e.Computer.HasLost) firstWins++;
        if (e.Player.LossReason.Contains("牌庫") || e.Computer.LossReason.Contains("牌庫")) fatigue++;
        stepsTotal += step; turnTotal += e.TurnNumber;
    }
    records.Add(new { player = db.PresetDecks[i].Name, opponent = db.PresetDecks[j].Name, games = wins + losses, wins, losses, firstWins, fatigue,
        averageTurns = Math.Round((double)turnTotal/(wins+losses),2), averageActions = Math.Round((double)stepsTotal/(wins+losses),2) });
}
var output = JsonSerializer.Serialize(new { policy = "same deterministic heuristic both sides; seeds 100 onward; mirrored first player; not human win rates", seeds, pairs = records }, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(output);
