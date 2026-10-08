using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Builder;
// 用法：dotnet run --project tools/RankedCheck -- [WcgWeb 路徑] [每副種子數]
// 檢查每副天梯牌組與所有微調都合法、能完整打完，並輸出對五副預組的 AI 對照勝率與各牌位平均。
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=Path.GetFullPath(args.Length>0?args[0]:"WcgWeb")});
var db=new CardDatabase(builder.Environment);var pool=new RankedDecks(db,builder.Environment);int seeds=args.Length>1?int.Parse(args[1]):6;
(bool won,int turns,double slowest) Play(Deck deck,int tier,int seed,bool first)
{
 var e=new GameEngine(db,new Random(700+seed));e.StartGame(deck,db.PresetDecks[seed%5],first);e.Player.IsAi=true;double slowest=0;
 for(int step=0;step<3000&&!e.IsOver;step++)
 {
  e.AiLevel=e.DecisionPlayerId=="player"?tier:2;var start=System.Diagnostics.Stopwatch.StartNew();
  if(!e.ExecuteAiStep())throw new Exception(deck.Name+" stalled: "+e.LastError);
  slowest=Math.Max(slowest,start.Elapsed.TotalMilliseconds);
 }
 if(!e.IsOver)throw new Exception("Timeout: "+deck.Name);
 if(new[]{e.Player,e.Computer}.Sum(p=>p.Hand.Count+p.Deck.Count+p.Graveyard.Count+p.EnergyZone.Count+p.Occupied+p.Field.Sum(m=>m.Attachments.Count)+p.Field.Count(m=>m.SilenceSpell!=null))!=100)
  throw new Exception("Card count changed: "+deck.Name);
 return(e.Computer.HasLost,e.TurnNumber,slowest);
}
var output=new List<object>();
foreach(var entry in pool.All)
{
 int wins=0,games=0,turns=0;double slowest=0;
 for(int seed=0;seed<seeds;seed++)foreach(bool first in new[]{true,false})
 {var r=Play(entry.Deck,entry.Tier,seed,first);games++;if(r.won)wins++;turns+=r.turns;slowest=Math.Max(slowest,r.slowest);}
 var variants=entry.Variants??[];
 for(int i=0;i<variants.Count;i++)
 {
  var changed=RankedDecks.Apply(entry.Deck,variants[i]);
  if(changed==null||!pool.IsLegal(changed,out var error))throw new Exception($"Illegal variant {entry.Deck.Name}/{variants[i].Name}");
  Play(changed,entry.Tier,i,i%2==0);
 }
 output.Add(new{tier=entry.Tier,deck=entry.Deck.Name,archetype=entry.Archetype,will=entry.Deck.MainWill,variants=variants.Count,games,wins,winRate=Math.Round((double)wins/games,3),averageTurns=Math.Round((double)turns/games,2),slowestActionMs=Math.Round(slowest,2)});
}
// 隨機抽選與微調：每個牌位抽 200 次，確認牌組合法且連續兩場不重複流派。
var picks=new List<object>();
for(int tier=0;tier<6;tier++)
{
 var rng=new Random(900+tier);string? last=null;var distinct=new HashSet<string>();int repeats=0;
 for(int i=0;i<200;i++)
 {
  var deck=pool.Pick(tier,rng,last);
  if(!pool.IsLegal(deck,out var error))throw new Exception("Illegal pick: "+deck.Name+" "+error);
  if(last!=null&&deck.Id==last)repeats++;last=deck.Id;distinct.Add(string.Join(',',deck.CardIds.Order()));
 }
 picks.Add(new{tier,pool=pool.Pool(tier).Count,distinctDecklistsIn200Picks=distinct.Count,consecutiveRepeats=repeats});
}
var tiers=output.Cast<dynamic>().GroupBy(r=>(int)r.tier).Select(g=>new{tier=g.Key,decks=g.Count(),games=g.Sum(r=>(int)r.games),winRate=Math.Round((double)g.Sum(r=>(int)r.wins)/g.Sum(r=>(int)r.games),3)});
Console.WriteLine(JsonSerializer.Serialize(new{policy="AI mirror tests, seeds 700 onward, both starting positions; ranked deck vs rotating presets with the same v0.6 AI; not human win rates",seeds,tiers,picks,results=output},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
