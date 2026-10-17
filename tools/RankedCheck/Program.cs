using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;
using WcgWeb.Models.Ranked;
using Microsoft.AspNetCore.Builder;
// 用法：dotnet run --project tools/RankedCheck -- [WcgWeb 路徑] [每副種子數] [--matrix|--ai-vs|--v7-ladder|--explain <代碼或存檔> [索引]]
// --matrix：只跑強度矩陣。每個牌位的對手池（該牌位電腦＋該牌位牌組）分別對上「玩家方」五副預組，
// 玩家方依序使用試玩電腦（-1）與青銅～大師分級電腦（0～5）；每格每流派 種子數 × 2（先後手）場。
// 檢查每副天梯牌組與所有微調都合法、能完整打完，並輸出對五副預組的 AI 對照勝率與各牌位平均。
// 天梯方使用該牌位的分級電腦（AiLevel = 牌位），預組方使用練習模式的 v0.6 電腦（AiLevel = -1）代表一般玩家。
var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=Path.GetFullPath(args.Length>0?args[0]:"WcgWeb")});
var db=new CardDatabase(builder.Environment);var pool=new RankedDecks(db,builder.Environment);int seeds=args.Length>1?int.Parse(args[1]):6;
// --explain <對局代碼 | 代碼檔 | 匯出存檔.json> [紀錄索引|match]：重新模擬一場天梯對局，逐步輸出電腦每個決策
// （必殺、計畫、能量、失誤、搜尋候選分數與對手回應評估）。紀錄索引預設為最後一場；match 表示進行中的對局。
if(args.Contains("--explain"))
{
 var at=Array.IndexOf(args,"--explain");var input=args[at+1];var which=args.Length>at+2?args[at+2]:"";
 RankedRecord? record;var text=File.Exists(input)?File.ReadAllText(input):input;
 if(text.TrimStart().StartsWith("{"))
 {
  var root=JsonDocument.Parse(text).RootElement;var ranked=root.TryGetProperty("data",out var data)?data.GetProperty("ranked").GetString()!:text;
  var profile=JsonSerializer.Deserialize<RankedProfile>(ranked)!;var records=profile.Records??[];
  if(which=="match"){var m=profile.Match??throw new Exception("No match in progress.");record=new RankedRecord{Id=m.Id,Tier=m.Tier,Seed=m.Seed,PlayerFirst=m.PlayerFirst,Rules=m.Rules,PlayerDeck=m.PlayerDeck,ComputerDeck=m.ComputerDeck,Actions=RankedRecord.Encode(m.Actions)};}
  else{var i=which==""?records.Count-1:int.Parse(which);if(i<0)i+=records.Count;record=records[i];}
 }
 else{record=MatchCode.Decode(text,out var err,out _);if(record==null)throw new Exception(err);}
 var matchCode=MatchCode.Encode(record);Console.WriteLine($"match code ({matchCode.Length} chars): {(args.Contains("--print-code")?matchCode:matchCode[..Math.Min(60,matchCode.Length)]+"…")}");
 var storage=new MemoryStorage();
 var session=new RankedSession(db,new DeckService(db,storage,pool),new RankedStore(storage),pool,Microsoft.Extensions.Logging.Abstractions.NullLogger<BattleCoordinator>.Instance,TimeProvider.System);
 var traces=new SortedDictionary<int,List<string>>();
 var replay=session.BuildReplay(record,(i,t)=>{if(!traces.TryGetValue(i,out var l))traces[i]=l=[];l.Add(t);});
 var actions=RankedRecord.Decode(record.Actions);
 Console.WriteLine($"{record.PlayerDeck.Name} vs {record.ComputerDeck.Name}  tier {record.Tier}  seed {record.Seed}  {(record.PlayerFirst?"player first":"computer first")}  {actions.Count} actions  rules {record.Rules[..Math.Min(24,record.Rules.Length)]}…");
 for(int i=0;i<actions.Count&&i+1<replay.Steps.Count;i++)
 {
  var a=actions[i];var st=replay.Steps[i+1].State;
  Console.WriteLine($"#{i} turn {st.Turn} {(a.Type=="ai"?"AI":"player")} {a.Type} src {a.Source} tgt {a.TargetSide}/{a.Target} choice {a.Choice} v{a.RulesVersion}  hp {st.Player.Hp}-{st.Computer.Hp}");
  if(traces.TryGetValue(i,out var lines))foreach(var l in lines)Console.WriteLine("    "+l);
  if(args.Contains("--boards"))
  {
   string Side(WcgWeb.Models.Battle.BattleSide s)=>string.Join(" ",s.Field.Select(m=>$"{m.Card.Name}({m.PP}/{m.DP}{(m.Tapped?",橫":"")}{(m.Status.Length>0?","+string.Join("/",m.Status):"")})"));
   Console.WriteLine($"    [玩家 HP{st.Player.Hp} 能量{st.Player.AvailableEnergy}/{st.Player.TotalEnergy} 手{st.Player.HandCount}] {Side(st.Player)}");
   Console.WriteLine($"    [電腦 HP{st.Computer.Hp} 能量{st.Computer.AvailableEnergy}/{st.Computer.TotalEnergy} 手{st.Computer.HandCount}] {Side(st.Computer)}");
   var prev=replay.Steps[i].State.Logs;foreach(var l in st.Logs.Skip(prev.Length>st.Logs.Length?0:prev.Length))Console.WriteLine("      log: "+l);
  }
 }
 if(replay.Error!="")Console.WriteLine("ERROR: "+replay.Error);
 return;
}
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
 if(new[]{e.Player,e.Computer}.Sum(p=>p.Hand.Count+p.Deck.Count+p.Graveyard.Count+p.EnergyZone.Count+p.Occupied+p.Field.Sum(m=>m.Attachments.Count)+p.Field.Count(m=>m.SilenceSpell!=null))!=100+e.OpeningCoins)
  throw new Exception("Card count changed: "+deck.Name);
 return(e.Computer.HasLost,e.TurnNumber,slowest);
}
// --ai-vs：AI v7 對 AI v6。每個牌位用五副預組全部配對（含鏡像、雙方先後手、雙方交換牌組），
// 流派以 v7 方的預組計。--v7-ladder：v7 牌位 n 對 v7 牌位 n−1，檢查分級單調變強。
// 決策耗時只計 v7 方的每一步（box 上的 .NET JIT；瀏覽器 WASM 會更慢）。
(bool v7won,List<double> ms) PlayVs(Deck v7Deck,Deck otherDeck,int seed,bool v7First,int v7Level,int v7Rev,int otherLevel,int otherRev)
{
 var e=new GameEngine(db,new Random(1700+seed));e.SeedRankedAi(1700+seed);e.StartGame(v7Deck,otherDeck,v7First);e.Player.IsAi=true;var ms=new List<double>();
 for(int step=0;step<3000&&!e.IsOver;step++)
 {
  bool mine=e.DecisionPlayerId=="player";e.AiLevel=mine?v7Level:otherLevel;e.AiRevision=mine?v7Rev:otherRev;
  var clock=System.Diagnostics.Stopwatch.StartNew();
  if(!e.ExecuteAiStep())throw new Exception($"stalled: {v7Deck.Name} vs {otherDeck.Name} seed {seed}: {e.LastError}");
  if(mine)ms.Add(clock.Elapsed.TotalMilliseconds);
 }
 if(!e.IsOver)throw new Exception("Timeout");
 return(e.Computer.HasLost,ms);
}
// --v8-vs-v7：同牌位 v8（目前版本）對 v7，五副預組全部配對、雙方先後手。
if(args.Contains("--v8-vs-v7"))
{
 int[] tierList=args.Contains("--tiers")?args[Array.IndexOf(args,"--tiers")+1].Split(',').Select(int.Parse).ToArray():[3,4,5];
 var jobs=tierList.SelectMany(t=>Enumerable.Range(0,5).SelectMany(i=>Enumerable.Range(0,5).SelectMany(j=>Enumerable.Range(0,seeds).SelectMany(s=>new[]{true,false}.Select(f=>(t,i,j,s,f)))))).ToList();
 var res=new (bool won,List<double> ms)[jobs.Count];var clock=System.Diagnostics.Stopwatch.StartNew();
 var threads=int.TryParse(Environment.GetEnvironmentVariable("RANKEDCHECK_THREADS"),out var n3)&&n3>0?n3:Environment.ProcessorCount;
 Parallel.For(0,jobs.Count,new ParallelOptions{MaxDegreeOfParallelism=threads},k=>{var (t,i,j,s,f)=jobs[k];res[k]=PlayVs(db.PresetDecks[i],db.PresetDecks[j],s*25+i*5+j,f,t,GameEngine.RankedAiVersion,t,7);});
 double Pct(List<double> xs,double q){if(xs.Count==0)return 0;var o=xs.OrderBy(x=>x).ToList();return Math.Round(o[Math.Min(o.Count-1,(int)(q*o.Count))],2);}
 var table=tierList.Select(t=>{var idx=Enumerable.Range(0,jobs.Count).Where(k=>jobs[k].t==t).ToList();var ms=idx.SelectMany(k=>res[k].ms).ToList();double wr=(double)idx.Count(k=>res[k].won)/idx.Count;
  return new{tier=t,tierName=RankedRules.Tiers[t],games=idx.Count,winRate=Math.Round(wr,3),ci95=Math.Round(1.96*Math.Sqrt(wr*(1-wr)/idx.Count),3),decisionMs=new{median=Pct(ms,.5),p95=Pct(ms,.95),p99=Pct(ms,.99),max=Math.Round(ms.DefaultIfEmpty(0).Max(),2)}};}).ToList();
 Console.WriteLine(JsonSerializer.Serialize(new{mode=$"v{GameEngine.RankedAiVersion} vs v7 (same tier)",seedsPerPairing=seeds,elapsedSeconds=Math.Round(clock.Elapsed.TotalSeconds,1),table},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
 return;
}
if(args.Contains("--ai-vs")||args.Contains("--v7-ladder"))
{
 bool ladderMode=args.Contains("--v7-ladder");int newRev=GameEngine.RankedAiVersion<7?7:GameEngine.RankedAiVersion;
 var tierList=ladderMode?new[]{1,2,3,4,5}:new[]{0,1,2,3,4,5};
 var jobs=tierList.SelectMany(t=>Enumerable.Range(0,5).SelectMany(i=>Enumerable.Range(0,5).SelectMany(j=>Enumerable.Range(0,seeds).SelectMany(s=>new[]{true,false}.Select(f=>(t,i,j,s,f)))))).ToList();
 var res=new (bool won,List<double> ms)[jobs.Count];var clock=System.Diagnostics.Stopwatch.StartNew();
 var threads=int.TryParse(Environment.GetEnvironmentVariable("RANKEDCHECK_THREADS"),out var n2)&&n2>0?n2:Environment.ProcessorCount;
 Parallel.For(0,jobs.Count,new ParallelOptions{MaxDegreeOfParallelism=threads},k=>
 {var (t,i,j,s,f)=jobs[k];res[k]=ladderMode?PlayVs(db.PresetDecks[i],db.PresetDecks[j],s*25+i*5+j,f,t,newRev,t-1,newRev):PlayVs(db.PresetDecks[i],db.PresetDecks[j],s*25+i*5+j,f,t,newRev,t,6);});
 double Pct(List<double> xs,double q){if(xs.Count==0)return 0;var o=xs.OrderBy(x=>x).ToList();return Math.Round(o[Math.Min(o.Count-1,(int)(q*o.Count))],2);}
 var table=tierList.Select(t=>
 {
  var idx=Enumerable.Range(0,jobs.Count).Where(k=>jobs[k].t==t).ToList();var ms=idx.SelectMany(k=>res[k].ms).ToList();
  double wr=(double)idx.Count(k=>res[k].won)/idx.Count;double ci=1.96*Math.Sqrt(wr*(1-wr)/idx.Count);
  return new{tier=t,tierName=RankedRules.Tiers[t],opponent=ladderMode?"v7 "+RankedRules.Tiers[t-1]:"v6 "+RankedRules.Tiers[t],games=idx.Count,winRate=Math.Round(wr,3),ci95=Math.Round(ci,3),
   archetypes=Enumerable.Range(0,5).Select(i=>{var a=idx.Where(k=>jobs[k].i==i).ToList();return new{deck=db.PresetDecks[i].Name,games=a.Count,winRate=Math.Round((double)a.Count(k=>res[k].won)/a.Count,3)};}).ToList(),
   decisionMs=new{decisions=ms.Count,median=Pct(ms,.5),p95=Pct(ms,.95),p99=Pct(ms,.99),max=Math.Round(ms.DefaultIfEmpty(0).Max(),2)}};
 }).ToList();
 Console.WriteLine(JsonSerializer.Serialize(new{mode=ladderMode?"v7 tier n vs v7 tier n-1":"v7 vs v6 (same tier)",v7Revision=newRev,seedsPerPairing=seeds,elapsedSeconds=Math.Round(clock.Elapsed.TotalSeconds,1),table},new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
 return;
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
// In-memory player storage for --explain (never touches real saves).
sealed class MemoryStorage:IPlayerStorage
{
 public object Gate{get;}=new();
 readonly Dictionary<string,string> values=new();
 public string? Read(string key)=>values.GetValueOrDefault(key);
 public void Write(string key,string value,string? expected){values[key]=value;}
}
