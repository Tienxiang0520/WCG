using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;

// Deterministic hill-climb of deck lists with Master v7 (AiLevel 5, RankedAiVersion) on both sides.
// Each candidate faces one fixed field (Master decks + the five presets) with common paired seeds and mirrored starts.
// Only card counts change; card stats/effects are untouched. Never reads or writes player data.
string Arg(string name,string fallback){var n=Array.IndexOf(args,name);return n<0?fallback:args[n+1];}
var root=Path.GetFullPath(Arg("--root","WcgWeb"));
var cardsJson=File.ReadAllText(Path.Combine(root,"Data/cards.json"));
var presetPath=Arg("--presets",Path.Combine(root,"Data/preset_decks.json"));
var cards=new CardDatabase(cardsJson,File.ReadAllText(presetPath));
var allIds=JsonDocument.Parse(cardsJson).RootElement.EnumerateArray().Select(e=>e.GetProperty("id").GetString()!).ToArray();
var presets=JsonSerializer.Deserialize<List<Deck>>(File.ReadAllText(presetPath))!;
var masters=JsonSerializer.Deserialize<List<RankedDecks.Entry>>(File.ReadAllText(Arg("--ranked",Path.Combine(root,"Data/ranked_decks.json"))))!.Where(e=>e.Tier==5).Select(e=>e.Deck).ToList();
var field=masters.Concat(presets).OrderBy(d=>d.Id,StringComparer.Ordinal).ToArray();
int threads=int.Parse(Arg("--threads","8")),seedBase=int.Parse(Arg("--seed-base","9100000"));
var mode=Arg("--mode","tune");
var targets=Arg("--targets","").Split(',',StringSplitOptions.RemoveEmptyEntries);
var opts=new JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
var clock=System.Diagnostics.Stopwatch.StartNew();long played=0;

// Wins of deck vs field for seed indexes [s0,s1); both starts; returns per-game outcomes in fixed order.
bool[] Play(Deck deck,string selfId,int s0,int s1)
{
    var jobs=(from o in field where o.Id!=selfId from s in Enumerable.Range(s0,s1-s0) from f in new[]{true,false} select (o,s,f)).ToArray();
    var res=new bool[jobs.Length];
    Parallel.For(0,jobs.Length,new ParallelOptions{MaxDegreeOfParallelism=threads},n=>{
        var(o,s,first)=jobs[n];
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes(selfId+"|"+o.Id));
        int seed=unchecked((int)(BitConverter.ToUInt32(hash,0)&0x7fffffff)+seedBase+s*7919);
        var engine=new GameEngine(cards,new Random(seed));engine.SeedRankedAi(seed);engine.AiLevel=5;
        engine.StartGame(deck,o,first);engine.Player.IsAi=true;
        for(var steps=0;steps<4000&&!engine.IsOver;steps++)if(!engine.ExecuteAiStep())throw new InvalidOperationException("Stalled: "+deck.Name+" / "+engine.LastError);
        if(!engine.IsOver)throw new InvalidOperationException("Action budget exceeded.");
        res[n]=engine.Computer.HasLost;Interlocked.Increment(ref played);
    });
    return res;
}
Deck Make(Deck b,Dictionary<string,int> c)=>new Deck{Id=b.Id,Name=b.Name,Description=b.Description,MainWill=b.MainWill,CardIds=c.OrderBy(k=>k.Key,StringComparer.Ordinal).SelectMany(k=>Enumerable.Repeat(k.Key,k.Value)).ToList()};
Dictionary<string,int> Count(Deck d)=>d.CardIds.GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count());
Deck Find(string id)=>field.First(d=>d.Id==id);
object Ci(bool[] r){double p=r.Count(x=>x)/(double)r.Length;return new{games=r.Length,wins=r.Count(x=>x),rate=Math.Round(p,4),ci95=Math.Round(1.96*Math.Sqrt(p*(1-p)/r.Length),4)};}

if(mode=="verify")
{   // --after file: JSON {id: [cardIds]}; compares original vs tuned list on fresh seeds.
    var after=JsonSerializer.Deserialize<Dictionary<string,List<string>>>(File.ReadAllText(Arg("--after","")))!;
    int seeds=int.Parse(Arg("--seeds","6"));var outList=new List<object>();
    foreach(var id in targets){var b0=Find(id);var b=Make(b0,Count(b0));var a=Make(b,after.TryGetValue(id,out var l)?l.GroupBy(x=>x).ToDictionary(g=>g.Key,g=>g.Count()):Count(b));
        if(!a.IsValid(cards.GetCard,out var err))throw new InvalidDataException(id+": "+err);
        var rb=Play(b,id,0,seeds);var ra=Play(a,id,0,seeds);
        int up=rb.Zip(ra).Count(z=>!z.First&&z.Second),down=rb.Zip(ra).Count(z=>z.First&&!z.Second);
        var dd=rb.Zip(ra).Select(z=>(z.Second?1.0:0)-(z.First?1:0)).ToArray();double m=dd.Average(),sd=Math.Sqrt(dd.Select(x=>(x-m)*(x-m)).Sum()/(dd.Length-1));
        var row=new{id,name=b.Name,before=Ci(rb),after=Ci(ra),delta=Math.Round(m,4),deltaCi95=Math.Round(1.96*sd/Math.Sqrt(dd.Length),4),flipsUp=up,flipsDown=down};
        outList.Add(row);Console.Error.WriteLine(JsonSerializer.Serialize(row,opts).Replace("\n"," "));}
    File.WriteAllText(Arg("--out","verify.json"),JsonSerializer.Serialize(new{seedBase,seeds,field=field.Select(d=>d.Id),rows=outList},opts));return;
}
if(mode=="time"){var d=Find(targets[0]);var r=Play(d,d.Id,0,1);Console.WriteLine($"{r.Length} games {clock.Elapsed.TotalSeconds:F1}s rate {r.Count(x=>x)/(double)r.Length:F3}");return;}

int maxSwaps=int.Parse(Arg("--max-swaps","4")),cands=int.Parse(Arg("--candidates","10")),screenSeeds=int.Parse(Arg("--screen-seeds","1")),confirmSeeds=int.Parse(Arg("--confirm-seeds","3"));
var results=new List<object>();
foreach(var id in targets)
{
    var orig=Find(id);var cur=Count(orig);var log=new List<object>();
    var fieldUse=field.Where(d=>d.Id!=id).SelectMany(d=>d.CardIds).Distinct().ToHashSet();
    int swaps=0,fails=0,round=0;
    var curScreen=Play(Make(orig,cur),id,0,screenSeeds);
    var rng=new Random(BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes("tune|"+id)),0));
    var tried=new HashSet<string>();
    while(swaps<maxSwaps&&fails<3&&round<8)
    {
        round++;
        var outs=cur.Where(k=>k.Value>=2).Select(k=>k.Key).OrderBy(k=>k,StringComparer.Ordinal).ToArray();
        var pool=allIds.Where(c=>cur.GetValueOrDefault(c)<=2).ToArray();
        var list=new List<(string o,string i,Deck d)>();
        for(int t=0;t<400&&list.Count<cands;t++)
        {
            var o=outs[rng.Next(outs.Length)];var preferred=pool.Where(fieldUse.Contains).ToArray();
            var i=rng.NextDouble()<.75&&preferred.Length>0?preferred[rng.Next(preferred.Length)]:pool[rng.Next(pool.Length)];
            if(o==i||!tried.Add(o+">"+i))continue;
            var c=new Dictionary<string,int>(cur);c[o]-=2;if(c[o]==0)c.Remove(o);c[i]=c.GetValueOrDefault(i)+2;
            // Identity: at most maxSwaps*2 cards differ from the original list.
            var oc=Count(orig);int diff=c.Sum(k=>Math.Max(0,k.Value-oc.GetValueOrDefault(k.Key)));if(diff>maxSwaps*2)continue;
            var d=Make(orig,c);if(!d.IsValid(cards.GetCard,out _))continue;list.Add((o,i,d));
        }
        var scored=list.Select(x=>(x.o,x.i,x.d,r:Play(x.d,id,0,screenSeeds))).OrderByDescending(x=>x.r.Count(y=>y)).ThenBy(x=>x.o+x.i,StringComparer.Ordinal).ToArray();
        int baseW=curScreen.Count(x=>x);
        Console.Error.WriteLine($"{id} r{round} base {baseW}/{curScreen.Length} best {(scored.Length>0?scored[0].r.Count(y=>y):-1)} ({played} games {clock.Elapsed.TotalSeconds:F0}s)");
        var accepted=false;
        foreach(var best in scored.Take(2).Where(x=>x.r.Count(y=>y)>baseW))
        {
            var cb=Play(Make(orig,cur),id,screenSeeds,screenSeeds+confirmSeeds);var ca=Play(best.d,id,screenSeeds,screenSeeds+confirmSeeds);
            int gain=ca.Count(x=>x)-cb.Count(x=>x);
            log.Add(new{round,outCard=best.o,inCard=best.i,screen=$"{best.r.Count(y=>y)} vs {baseW} /{curScreen.Length}",confirm=$"{ca.Count(x=>x)} vs {cb.Count(x=>x)} /{ca.Length}",accepted=gain>0});
            if(gain>0){cur=Count(best.d);curScreen=best.r;swaps++;accepted=true;break;}
        }
        if(!accepted)fails++;else fails=0;
    }
    var final=Make(orig,cur);var oc2=Count(orig);
    results.Add(new{id,name=orig.Name,cardIds=final.CardIds,removed=oc2.Where(k=>k.Value>cur.GetValueOrDefault(k.Key)).ToDictionary(k=>k.Key,k=>k.Value-cur.GetValueOrDefault(k.Key)),added=cur.Where(k=>k.Value>oc2.GetValueOrDefault(k.Key)).ToDictionary(k=>k.Key,k=>k.Value-oc2.GetValueOrDefault(k.Key)),log});
    File.WriteAllText(Arg("--out","tune.json"),JsonSerializer.Serialize(new{seedBase,games=played,results},opts));
}
Console.WriteLine($"done {played} games {clock.Elapsed.TotalSeconds:F0}s");
