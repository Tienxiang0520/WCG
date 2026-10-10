using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;

// Same-strength round robin. No browser/player storage is opened or changed.
// --decks PATH --out PATH [--cards PATH] [--tier 5] [--ai 5] [--seeds 8] [--seed-base 10000] [--threads 4]
string Arg(string name,string fallback) {var n=Array.IndexOf(args,name);return n<0?fallback:args[n+1];}
var root=Path.GetFullPath(Arg("--root","WcgWeb"));
var catalog=new CardDatabase(File.ReadAllText(Arg("--cards",Path.Combine(root,"Data/cards.json"))),File.ReadAllText(Path.Combine(root,"Data/preset_decks.json")));
var entries=JsonSerializer.Deserialize<List<RankedDecks.Entry>>(File.ReadAllText(Arg("--decks",Path.Combine(root,"Data/ranked_decks.json"))))!;
int tier=int.Parse(Arg("--tier","5")),ai=int.Parse(Arg("--ai","5")),seeds=int.Parse(Arg("--seeds","8")),seedBase=int.Parse(Arg("--seed-base","10000"));
var pool=entries.Where(e=>e.Tier==tier).OrderBy(e=>e.Deck.Id,StringComparer.Ordinal).ToArray();
if(pool.Length<2||seeds<1)throw new ArgumentException("Need at least two decks and a positive seed count.");
foreach(var e in pool)if(!e.Deck.IsValid(catalog.GetCard,out var error))throw new InvalidDataException(e.Deck.Name+": "+error);
var jobs=(from i in Enumerable.Range(0,pool.Length) from j in Enumerable.Range(i+1,pool.Length-i-1) select (i,j)).ToArray();
var results=new PairResult[jobs.Length];int done=0;
var timer=System.Diagnostics.Stopwatch.StartNew();
Parallel.For(0,jobs.Length,new ParallelOptions{MaxDegreeOfParallelism=int.Parse(Arg("--threads","4"))},k=>{
    var(i,j)=jobs[k];int leftWins=0,firstWins=0,turns=0,actions=0,fatigue=0;
    var hash=SHA256.HashData(Encoding.UTF8.GetBytes(pool[i].Deck.Id+"|"+pool[j].Deck.Id));
    var pairSeed=(int)(BitConverter.ToUInt32(hash,0)&0x7fffffff);
    for(int s=0;s<seeds;s++)foreach(var first in new[]{true,false}){
        int seed=unchecked(pairSeed+seedBase+s*7919);
        var g=new GameEngine(catalog,new Random(seed));g.SeedRankedAi(seed);g.AiLevel=ai;
        g.StartGame(pool[i].Deck,pool[j].Deck,first);g.Player.IsAi=true;int steps=0;
        for(;steps<4000&&!g.IsOver;steps++)if(!g.ExecuteAiStep())throw new InvalidOperationException($"Stalled {pool[i].Deck.Name}/{pool[j].Deck.Name}: {g.LastError}");
        if(!g.IsOver)throw new InvalidOperationException("Action limit reached.");
        int count=new[]{g.Player,g.Computer}.Sum(p=>p.Hand.Count+p.Deck.Count+p.Graveyard.Count+p.EnergyZone.Count+p.Occupied+p.Field.Sum(m=>m.Attachments.Count)+p.Field.Count(m=>m.SilenceSpell!=null));
        if(count!=100)throw new InvalidOperationException("Card conservation failed: "+count);
        bool won=g.Computer.HasLost;if(won)leftWins++;if(won==first)firstWins++;
        turns+=g.TurnNumber;actions+=steps;if(g.Player.LossReason.Contains("牌庫")||g.Computer.LossReason.Contains("牌庫"))fatigue++;
    }
    results[k]=new(pool[i].Deck.Id,pool[j].Deck.Id,seeds*2,leftWins,firstWins,turns,actions,fatigue);
    int n=Interlocked.Increment(ref done);if(n%10==0||n==jobs.Length)Console.Error.WriteLine($"Pairs {n}/{jobs.Length} ({timer.Elapsed.TotalSeconds:F1}s)");
});
var summary=pool.Select(e=>{
    int wins=0,games=0;foreach(var r in results){if(r.Left==e.Deck.Id){wins+=r.LeftWins;games+=r.Games;}else if(r.Right==e.Deck.Id){wins+=r.Games-r.LeftWins;games+=r.Games;}}
    double p=(double)wins/games,z=1.96,d=1+z*z/games,c=(p+z*z/(2*games))/d,h=z*Math.Sqrt(p*(1-p)/games+z*z/(4d*games*games))/d;
    return new{e.Deck.Id,e.Deck.Name,e.Deck.MainWill,e.Archetype,games,wins,winRate=Math.Round(p,4),ciLow=Math.Round(c-h,4),ciHigh=Math.Round(c+h,4),cards=e.Deck.CardIds.GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count())};
}).OrderByDescending(d=>d.winRate).ToArray();
var output=new{policy="Same AI for both sides, all unordered pairs, mirrored starting player and paired deterministic seeds. AI simulation, not human win rate; card/deck association is not causal.",tier,ai,seeds,seedBase,elapsedSeconds=Math.Round(timer.Elapsed.TotalSeconds,1),games=results.Sum(r=>r.Games),firstPlayerWinRate=(double)results.Sum(r=>r.FirstWins)/results.Sum(r=>r.Games),summary,pairs=results};
var path=Path.GetFullPath(Arg("--out","output/balance-league.json"));Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.WriteAllText(path,JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
Console.WriteLine($"{output.games} games, {pool.Length} decks, first-player wins {output.firstPlayerWinRate:P1}; saved {path}");
record PairResult(string Left,string Right,int Games,int LeftWins,int FirstWins,int Turns,int Actions,int Fatigue);
