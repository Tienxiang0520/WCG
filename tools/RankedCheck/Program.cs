using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;
using WcgWeb.Models.Ranked;
using Microsoft.AspNetCore.Builder;
// 用法：dotnet run --project tools/RankedCheck -- [WcgWeb 路徑] [每副種子數] [--matrix]
// --matrix：只跑強度矩陣。每個牌位的對手池（該牌位電腦＋該牌位牌組）分別對上「玩家方」五副預組，
// 玩家方依序使用試玩電腦（-1）與青銅～大師分級電腦（0～5）；每格每流派 種子數 × 2（先後手）場。
// 檢查每副天梯牌組與所有微調都合法、能完整打完，並輸出對五副預組的 AI 對照勝率與各牌位平均。
// 天梯方使用該牌位的分級電腦（AiLevel = 牌位），預組方使用練習模式的 v0.6 電腦（AiLevel = -1）代表一般玩家。
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=Path.GetFullPath(args.Length>0?args[0]:"WcgWeb")});
var db=new CardDatabase(builder.Environment);var pool=new RankedDecks(db,builder.Environment);int seeds=args.Length>1?int.Parse(args[1]):6;
(bool won,int turns,double slowest) Play(Deck deck,int tier,int seed,bool first,Deck? opponent=null,int presetLevel=-1)
{
 var e=new GameEngine(db,new Random(700+seed));e.SeedRankedAi(700+seed);e.StartGame(deck,opponent??db.PresetDecks[seed%5],first);e.Player.IsAi=true;double slowest=0;
 for(int step=0;step<3000&&!e.IsOver;step++)
 {
  e.AiLevel=e.DecisionPlayerId=="player"?tier:presetLevel;var start=System.Diagnostics.Stopwatch.StartNew();
  if(!e.ExecuteAiStep())throw new Exception(deck.Name+" stalled: "+e.LastError);
  slowest=Math.Max(slowest,start.Elapsed.TotalMilliseconds);
 }
 if(!e.IsOver)throw new Exception("Timeout: "+deck.Name);
 if(new[]{e.Player,e.Computer}.Sum(p=>p.Hand.Count+p.Deck.Count+p.Graveyard.Count+p.EnergyZone.Count+p.Occupied+p.Field.Sum(m=>m.Attachments.Count)+p.Field.Count(m=>m.SilenceSpell!=null))!=100)
  throw new Exception("Card count changed: "+deck.Name);
 return(e.Computer.HasLost,e.TurnNumber,slowest);
}
if(args.Contains("--matrix"))
{
 int[] levels=[-1,0,1,2,3,4,5];string LevelName(int l)=>l<0?"試玩":RankedRules.Tiers[l];
 var jobs=pool.All.SelectMany(entry=>levels.Select(level=>(entry,level))).ToList();
 var results=new (int wins,int games)[jobs.Count];double slowestMs=0;object gate=new();
 var clock=System.Diagnostics.Stopwatch.StartNew();
 var threads=int.TryParse(Environment.GetEnvironmentVariable("RANKEDCHECK_THREADS"),out var n)&&n>0?n:Environment.ProcessorCount;
 Parallel.For(0,jobs.Count,new ParallelOptions{MaxDegreeOfParallelism=threads},k=>
 {
  var (entry,level)=jobs[k];int wins=0,games=0;double slow=0;
  for(int seed=0;seed<seeds;seed++)foreach(bool first in new[]{true,false})
  {var r=Play(entry.Deck,entry.Tier,seed,first,null,level);games++;if(r.won)wins++;slow=Math.Max(slow,r.slowest);}
  results[k]=(wins,games);lock(gate)slowestMs=Math.Max(slowestMs,slow);
 });
 var rows=jobs.Select((j,k)=>new{tier=j.entry.Tier,playerLevel=j.level,deck=j.entry.Deck.Name,results[k].wins,results[k].games}).ToList();
 var cells=rows.GroupBy(r=>(r.tier,r.playerLevel)).OrderBy(g=>g.Key.tier).ThenBy(g=>g.Key.playerLevel).Select(g=>
 {
  var sorted=g.OrderBy(r=>(double)r.wins/r.games).ToList();
  return new{opponentTier=g.Key.tier,opponentTierName=RankedRules.Tiers[g.Key.tier],playerLevel=g.Key.playerLevel,playerAi=LevelName(g.Key.playerLevel),
   decks=g.Count(),games=g.Sum(r=>r.games),opponentWinRate=Math.Round((double)g.Sum(r=>r.wins)/g.Sum(r=>r.games),3),
   weakest=new{deck=sorted[0].deck,winRate=Math.Round((double)sorted[0].wins/sorted[0].games,3)},
   strongest=new{deck=sorted[^1].deck,winRate=Math.Round((double)sorted[^1].wins/sorted[^1].games,3)},
   archetypes=sorted.Select(r=>new{r.deck,winRate=Math.Round((double)r.wins/r.games,3)}).ToList()};
 }).ToList();
 Console.WriteLine(JsonSerializer.Serialize(new{policy="opponent = ranked tier AI + that tier's decks; player = 5 preset decks (seed % 5) driven by the trial AI (-1) or tiered AI 0..5; seeds 700 onward, both starting positions; win rates are the opponent's",
  seedsPerArchetypePerCell=seeds,gamesPerArchetypePerCell=seeds*2,elapsedSeconds=Math.Round(clock.Elapsed.TotalSeconds,1),slowestActionMs=Math.Round(slowestMs,2),cells},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
 return;
}
// 分級電腦本身的強度：五副預組互打（25 組、雙方先後手），只有天梯方換成各牌位電腦。
var ladder=new List<object>();double worst=0;
for(int tier=0;tier<6;tier++)
{
 int wins=0,games=0;
 for(int i=0;i<5;i++)for(int j=0;j<5;j++)for(int seed=0;seed<Math.Max(2,seeds/5);seed++)foreach(bool first in new[]{true,false})
 {var r=Play(db.PresetDecks[i],tier,seed*31+i*5+j,first,db.PresetDecks[j]);games++;if(r.won)wins++;worst=Math.Max(worst,r.slowest);}
 ladder.Add(new{tier,games,winRateVsTrialAi=Math.Round((double)wins/games,3)});
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
var tiers=output.Cast<dynamic>().GroupBy(r=>(int)r.tier).Select(g=>{var sorted=g.OrderBy(r=>(double)r.winRate).ToList();return new{tier=g.Key,decks=g.Count(),games=g.Sum(r=>(int)r.games),winRate=Math.Round((double)g.Sum(r=>(int)r.wins)/g.Sum(r=>(int)r.games),3),
 wills=g.Select(r=>(string)r.will).Distinct().Count(),weakest=new{deck=(string)sorted[0].deck,winRate=(double)sorted[0].winRate},strongest=new{deck=(string)sorted[^1].deck,winRate=(double)sorted[^1].winRate},archetypes=sorted.Select(r=>(string)r.deck).ToList()};});
Console.WriteLine(JsonSerializer.Serialize(new{policy="seeds 700 onward, both starting positions; ranked side uses the tiered AI for its tier, presets use the v0.6 trial AI; not human win rates",seeds,aiLadder=ladder,aiLadderSlowestActionMs=Math.Round(worst,2),tiers,picks,results=output},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
