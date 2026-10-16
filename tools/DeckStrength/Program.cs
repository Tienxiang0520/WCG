using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;

// Compare build candidates against one fixed, equally represented archetype pool.
// All candidates of an archetype reuse seeds, opponents, both starts and both AI strategies.
string Arg(string name,string fallback) {var n=Array.IndexOf(args,name);return n<0?fallback:args[n+1];}
var root=Path.GetFullPath(Arg("--root","WcgWeb"));
var cards=new CardDatabase(File.ReadAllText(Path.Combine(root,"Data/cards.json")),File.ReadAllText(Path.Combine(root,"Data/preset_decks.json")));
var groups=JsonSerializer.Deserialize<List<Group>>(File.ReadAllText(Arg("--candidates","output/balance/2026-10-13-strongest/candidates.json")))!;
var opponents=JsonSerializer.Deserialize<List<RankedDecks.Entry>>(File.ReadAllText(Arg("--opponents","WcgWeb/Data/ranked_decks.json")))!.Where(e=>e.Tier==5).Select(e=>e.Deck).OrderBy(d=>d.Id,StringComparer.Ordinal).ToArray();
int seeds=int.Parse(Arg("--seeds","2")),seedBase=int.Parse(Arg("--seed-base","5300000"));
if(seeds<1||groups.Count==0||opponents.Length<2)throw new ArgumentException("Positive seeds and nonempty candidate/opponent pools required.");
foreach(var deck in groups.SelectMany(g=>g.Candidates.Select(c=>c.Deck)).Concat(opponents))
{
    if(!deck.IsValid(cards.GetCard,out var error))throw new InvalidDataException(deck.Name+": "+error);
}
var jobs=groups.SelectMany(g=>g.Candidates.Select((c,i)=>(g,c,i))).ToArray();
var results=new Result[jobs.Length];int done=0;
var clock=System.Diagnostics.Stopwatch.StartNew();
Parallel.For(0,jobs.Length,new ParallelOptions{MaxDegreeOfParallelism=int.Parse(Arg("--threads","4"))},n=>{
    var(group,candidate,index)=jobs[n];int totalWins=0,totalGames=0,totalFirst=0;
    var strategies=new List<Strategy>();
    foreach(var ai in new[]{5,3})
    {
        int wins=0,games=0;
        foreach(var opponent in opponents.Where(d=>d.Id!=group.Id))
        {
            var hash=SHA256.HashData(Encoding.UTF8.GetBytes(group.Id+"|"+opponent.Id));
            var pairSeed=(int)(BitConverter.ToUInt32(hash,0)&0x7fffffff);
            for(var s=0;s<seeds;s++)foreach(var first in new[]{true,false})
            {
                int seed=unchecked(pairSeed+seedBase+s*7919);
                var engine=new GameEngine(cards,new Random(seed));engine.SeedRankedAi(seed);engine.AiLevel=ai;
                engine.StartGame(candidate.Deck,opponent,first);engine.Player.IsAi=true;
                for(var steps=0;steps<4000&&!engine.IsOver;steps++)if(!engine.ExecuteAiStep())throw new InvalidOperationException("Stalled: "+candidate.Deck.Name+" / "+engine.LastError);
                if(!engine.IsOver)throw new InvalidOperationException("Action budget exceeded.");
                var count=new[]{engine.Player,engine.Computer}.Sum(p=>p.Hand.Count+p.Deck.Count+p.Graveyard.Count+p.EnergyZone.Count+p.Occupied+p.Field.Sum(m=>m.Attachments.Count)+p.Field.Count(m=>m.SilenceSpell!=null));
                if(count!=100)throw new InvalidOperationException("Card conservation failed.");
                bool won=engine.Computer.HasLost;if(won)wins++;if(won==first)totalFirst++;games++;
            }
        }
        totalWins+=wins;totalGames+=games;strategies.Add(new(ai,games,wins,(double)wins/games));
    }
    results[n]=new(group.Id,group.Name,index,candidate.Label,candidate.Deck,totalGames,totalWins,(double)totalWins/totalGames,totalFirst,strategies);
    int complete=Interlocked.Increment(ref done);if(complete%20==0||complete==jobs.Length)Console.Error.WriteLine($"Builds {complete}/{jobs.Length} ({clock.Elapsed.TotalSeconds:F1}s)");
});
var selected=results.GroupBy(r=>r.GroupId).Select(g=>g.OrderByDescending(r=>r.WinRate).ThenByDescending(r=>r.Strategies.Min(s=>s.WinRate)).ThenBy(r=>r.Index).First()).ToArray();
var output=new{policy="Bounded candidate search, not proof of a globally optimal or human-optimal deck. Fixed equally represented opponents, common paired seeds, mirrored starts, same AI on both sides, AI 5 and 3 weighted equally. Never modifies player data.",seeds,seedBase,games=results.Sum(r=>r.Games),elapsedSeconds=clock.Elapsed.TotalSeconds,selected,results};
var path=Path.GetFullPath(Arg("--out","output/deck-strength.json"));Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.WriteAllText(path,JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
Console.WriteLine($"{jobs.Length} builds, {output.games} games, saved {path}");
record Candidate(string Label,Deck Deck);
record Group(string Id,string Name,List<Candidate> Candidates);
record Strategy(int Ai,int Games,int Wins,double WinRate);
record Result(string GroupId,string Name,int Index,string Label,Deck Deck,int Games,int Wins,double WinRate,int FirstWins,List<Strategy> Strategies);
