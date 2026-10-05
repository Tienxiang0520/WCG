using System.Text.Json;
using LcgWeb.Services;
using Microsoft.AspNetCore.Builder;
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=Path.GetFullPath(args.Length>0?args[0]:"LcgWeb")});
var db=new CardDatabase(builder.Environment);var pool=new RankedDecks(db,builder.Environment);int seeds=args.Length>1?int.Parse(args[1]):6;
var output=new List<object>();
foreach(var entry in pool.All)
{
 int wins=0,games=0,turns=0;double slowest=0;
 for(int seed=0;seed<seeds;seed++)foreach(bool first in new[]{true,false})
 {
  var e=new GameEngine(db,new Random(700+seed));e.StartGame(entry.Deck,db.PresetDecks[seed%5],first);e.Player.IsAi=true;
  for(int step=0;step<3000&&!e.IsOver;step++)
  {
   e.AiLevel=e.DecisionPlayerId=="player"?entry.Tier:2;var start=System.Diagnostics.Stopwatch.StartNew();
   if(!e.ExecuteAiStep())throw new Exception(entry.Deck.Name+" stalled: "+e.LastError);
   slowest=Math.Max(slowest,start.Elapsed.TotalMilliseconds);
  }
  if(!e.IsOver)throw new Exception("Timeout: "+entry.Deck.Name);
  games++;if(e.Computer.HasLost)wins++;turns+=e.TurnNumber;
 }
 output.Add(new{tier=entry.Tier,deck=entry.Deck.Name,will=entry.Deck.MainWill,games,wins,averageTurns=(double)turns/games,slowestActionMs=slowest});
}
Console.WriteLine(JsonSerializer.Serialize(new{policy="AI mirror tests, seeds 700 onward, both starting positions; ranked deck+policy vs rotating presets at level 2; not human win rates",results=output},new JsonSerializerOptions{WriteIndented=true}));
