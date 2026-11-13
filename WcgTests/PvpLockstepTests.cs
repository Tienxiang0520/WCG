using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Models.Ranked;
using WcgWeb.Services;
using WcgWeb.Services.Pvp;

namespace WcgTests;

// PvP lockstep (Services/Pvp/PvpLockstep.cs): a reference engine plays both seats with the ranked AI; every move it
// makes is captured in the wire format, applied by the host client (its own seat) or the guest client, sent to the
// other client and checked against the sender's post-move hash. Host, guest and reference must never disagree.
public class PvpLockstepTests
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WcgWeb", "Data");
    private static readonly CardDatabase Cards = new(File.ReadAllText(Path.Combine(Data, "cards.json")), File.ReadAllText(Path.Combine(Data, "preset_decks.json")));
    private static readonly List<RankedDecks.Entry> Pool = JsonSerializer.Deserialize<List<RankedDecks.Entry>>(File.ReadAllText(Path.Combine(Data, "ranked_decks.json")))!
        .OrderBy(e => e.Deck.Id, StringComparer.Ordinal).ToList();

    private sealed class Pipe : IPvpMoveSink, IPvpMoveSource
    {
        public event Action<PvpMove>? Received;
        public readonly List<PvpMove> Sent = [];
        public void Send(PvpMove move) { Sent.Add(move); Received?.Invoke(move); }
    }

    private static PvpHandshake Shake(int seed, bool hostFirst, Deck host, Deck guest) =>
        new(PvpHandshake.CurrentVersion, "test", "rules", RankedRules.CurrentStartRules, seed, hostFirst, DeckCode.Encode(host), DeckCode.Encode(guest));

    private sealed record Outcome(int Moves, bool Over, string Hash, int Fallbacks, List<PvpMove> Stream, bool Reveal = false, bool Shuffle = false);

    // Plays one full match. Returns the move stream; throws on any disagreement.
    private static Outcome Play(int seed, bool hostFirst, Deck hostDeck, Deck guestDeck, int tier)
    {
        var shake = Shake(seed, hostFirst, hostDeck, guestDeck);
        var toGuest = new Pipe(); var toHost = new Pipe();
        var host = new PvpLockstep(Cards, shake, hostDeck, guestDeck, PvpSeat.Host, toGuest, toHost);
        var guest = new PvpLockstep(Cards, shake, hostDeck, guestDeck, PvpSeat.Guest, toHost, toGuest);
        var reference = new PvpLockstep(Cards, shake, hostDeck, guestDeck, PvpSeat.Host);
        reference.Engine.Player.IsAi = true; reference.Engine.Computer.IsAi = true;
        reference.Engine.AiLevel = tier; reference.Engine.SeedRankedAi(seed);
        Assert.Equal(host.Engine.StateHash(), guest.Engine.StateHash());
        Assert.Equal(host.Engine.StateHash(), reference.Engine.StateHash());
        var stream = new List<PvpMove>(); int fallbacks = 0; bool reveal = false, shuffle = false, random = false;
        for (int step = 0; step < 4000 && !reference.Engine.IsOver; step++)
        {
            var seat = reference.ToMove!.Value;
            var made = reference.ApplyAs(seat, g => g.ExecuteAiStep(), 1, send: false);
            if (made == null)
            {
                Assert.False(reference.Broken && reference.Failure == "", "reference");
                if (reference.Broken) { fallbacks++; break; } // AI step not expressible as one move: stop this match
                throw new InvalidOperationException($"AI stalled at {step}: {reference.Engine.LastError}");
            }
            var actor = seat == PvpSeat.Host ? host : guest;
            var other = seat == PvpSeat.Host ? guest : host;
            var sent = actor.ApplyLocalMove(made.Move);
            Assert.NotNull(sent);
            Assert.False(other.Broken, other.Failure);
            Assert.Equal(made.Seq, sent!.Seq);
            Assert.Equal(made.StateHash, sent.StateHash);
            Assert.Equal(sent.Seq, other.LastSeq);
            stream.Add(sent);
            var rev = host.Engine.Revision;
            foreach (var l in host.Engine.Logs.TakeWhile(l => l.ActionNumber == rev))
            { reveal |= l.Message.Contains("翻開牌庫頂"); shuffle |= l.Message.Contains("洗回牌庫") || l.Message.Contains("搜尋"); }
            random |= host.Engine.PresentationEvents.Any(e => e.Label == "棄牌代價");
        }
        Assert.Equal(reference.Engine.StateHash(), host.Engine.StateHash());
        Assert.Equal(host.Engine.StateHash(), guest.Engine.StateHash());
        // Mirrored views: each client's "me" is the other's "them".
        Assert.Equal(host.Engine.SeatHash(host.View.Me), guest.Engine.SeatHash(guest.View.Them));
        Assert.Equal(host.Engine.SeatHash(host.View.Them), guest.Engine.SeatHash(guest.View.Me));
        return new(stream.Count, host.Engine.IsOver, host.Engine.StateHash(), fallbacks, stream, reveal, shuffle || random);
    }

    [Fact]
    public void HandshakeFingerprintCoversEveryField()
    {
        var d = Pool[0].Deck; var a = Shake(1, true, d, Pool[1].Deck);
        Assert.Equal(a.Fingerprint(), (a with { }).Fingerprint());
        foreach (var b in new[] { a with { Seed = 2 }, a with { HostFirst = false }, a with { Rules = "x" }, a with { Build = "x" }, a with { StartRules = 1 }, a with { GuestDeck = a.HostDeck } })
            Assert.NotEqual(a.Fingerprint(), b.Fingerprint());
    }

    [Fact]
    public void SeatViewMirrorsTheGuest()
    {
        var shake = Shake(7, true, Pool[0].Deck, Pool[1].Deck);
        var host = new PvpLockstep(Cards, shake, Pool[0].Deck, Pool[1].Deck, PvpSeat.Host);
        var guest = new PvpLockstep(Cards, shake, Pool[0].Deck, Pool[1].Deck, PvpSeat.Guest);
        Assert.Same(host.Engine.Player, host.View.Me); Assert.Same(guest.Engine.Computer, guest.View.Me);
        Assert.Equal(2, host.View.HeroSide(mine: true)); Assert.Equal(3, guest.View.HeroSide(mine: true));
        Assert.Equal(3, host.View.HeroSide(mine: false)); Assert.Equal(2, guest.View.HeroSide(mine: false));
        Assert.True(host.View.IsMyDecision); Assert.False(guest.View.IsMyDecision);
        Assert.False(host.Engine.Player.IsAi || host.Engine.Computer.IsAi);
        // Each client sees its own deck list as "me".
        Assert.Equal(host.Engine.SeatHash(host.View.Me), guest.Engine.SeatHash(guest.View.Them));
    }

    [Fact]
    public void DirectPlayerCommandsCaptureAndStayInStep()
    {
        var toGuest = new Pipe();
        var shake = Shake(11, true, Pool[2].Deck, Pool[3].Deck);
        var host = new PvpLockstep(Cards, shake, Pool[2].Deck, Pool[3].Deck, PvpSeat.Host, toGuest);
        var guest = new PvpLockstep(Cards, shake, Pool[2].Deck, Pool[3].Deck, PvpSeat.Guest, source: toGuest);
        var me = host.View.Me;
        var energy = me.Hand.First(c => host.Engine.GetEnergyProblem(me, c) == "");
        Assert.NotNull(host.ApplyLocal(g => g.PlayEnergy(me, energy)));
        Assert.NotNull(host.ApplyLocal(g => g.EndTurn()));
        Assert.Equal(2, toGuest.Sent.Count);
        Assert.Equal(2, guest.LastSeq); Assert.False(guest.Broken, guest.Failure);
        Assert.Equal(host.Engine.StateHash(), guest.Engine.StateHash());
        Assert.True(guest.View.IsMyDecision);
        // Not your turn: nothing is applied or sent.
        Assert.Null(host.ApplyLocal(g => g.EndTurn()));
        Assert.Equal(2, toGuest.Sent.Count);
    }

    [Fact]
    public void TamperedMovesAreDetected()
    {
        var o = Play(101, true, Pool[4].Deck, Pool[5].Deck, 5);
        Assert.True(o.Moves > 20);
        var shake = Shake(101, true, Pool[4].Deck, Pool[5].Deck);
        PvpLockstep Fresh(PvpSeat s) => new(Cards, shake, Pool[4].Deck, Pool[5].Deck, s);
        int k = o.Stream.FindIndex(m => m.Seat == PvpSeat.Host && m.Move.Type == "aic:attack");
        if (k < 0) k = o.Stream.FindIndex(m => m.Seat == PvpSeat.Host);

        // 1. Wrong hash (the sender's engine diverged): Desync on that move.
        var g1 = Fresh(PvpSeat.Guest); var h1 = Fresh(PvpSeat.Host);
        var r = Feed(h1, g1, o.Stream, k, m => m with { StateHash = "00" });
        Assert.Equal(PvpApplyResult.Desync, r); Assert.True(g1.Broken);
        Assert.Equal(PvpApplyResult.Rejected, g1.ApplyRemote(o.Stream[k + 1]));

        // 2. A different but legal-looking move with the honest hash: the receiver applies it and the hash differs.
        var g2 = Fresh(PvpSeat.Guest); var h2 = Fresh(PvpSeat.Host);
        int e = o.Stream.FindIndex(m => m.Seat == PvpSeat.Host && m.Move.Type == "aic:end");
        r = Feed(h2, g2, o.Stream, e, m => m with { Move = new RankedAction("aic:end", RulesVersion: m.Move.RulesVersion) });
        Assert.Equal(PvpApplyResult.Ok, r); // ending the turn is the honest move here: same state
        var g3 = Fresh(PvpSeat.Guest); var h3 = Fresh(PvpSeat.Host);
        int p = o.Stream.FindIndex(m => m.Seat == PvpSeat.Host && m.Move.Type == "aic:energy");
        r = Feed(h3, g3, o.Stream, p, m => new PvpMove(m.Seq, m.Seat, new RankedAction("aic:end", RulesVersion: m.Move.RulesVersion), m.StateHash));
        Assert.Equal(PvpApplyResult.Desync, r);

        // 3. Out of order, wrong seat, garbage positions.
        var g4 = Fresh(PvpSeat.Guest);
        Assert.Equal(PvpApplyResult.OutOfOrder, g4.ApplyRemote(o.Stream[0] with { Seq = 2 }));
        var g5 = Fresh(PvpSeat.Guest);
        Assert.Equal(PvpApplyResult.WrongSeat, g5.ApplyRemote(o.Stream[0] with { Seat = PvpSeat.Guest }));
        var g6 = Fresh(PvpSeat.Guest);
        Assert.Equal(PvpApplyResult.Rejected, g6.ApplyRemote(o.Stream[0] with { Move = new RankedAction("aic:energy", Source: 99) }));
    }

    [Fact]
    public void HashSeesHiddenState()
    {
        var shake = Shake(5, true, Pool[0].Deck, Pool[1].Deck);
        var a = new PvpLockstep(Cards, shake, Pool[0].Deck, Pool[1].Deck, PvpSeat.Host);
        var b = new PvpLockstep(Cards, shake, Pool[0].Deck, Pool[1].Deck, PvpSeat.Guest);
        Assert.Equal(a.Engine.StateHash(), b.Engine.StateHash());
        Assert.Equal(a.Engine.StateHash(), a.Engine.StateHash()); // hashing does not change or consume anything
        var deck = b.Engine.Computer.Deck; int j = deck.FindIndex(c => c.Card.Id != deck[0].Card.Id);
        (deck[0], deck[j]) = (deck[j], deck[0]);
        Assert.NotEqual(a.Engine.StateHash(), b.Engine.StateHash());
        (deck[0], deck[j]) = (deck[j], deck[0]);
        b.Engine.Player.Field.Count.ToString(); b.Engine.Player.Hand[0].IsTapped = !b.Engine.Player.Hand[0].IsTapped;
        Assert.NotEqual(a.Engine.StateHash(), b.Engine.StateHash());
    }

    // Applies stream[..k] honestly (each seat's own client applies its own moves, the other receives), then the
    // tampered stream[k] reaches the guest. Returns the guest's result for the tampered move.
    private static PvpApplyResult Feed(PvpLockstep host, PvpLockstep guest, List<PvpMove> stream, int k, Func<PvpMove, PvpMove> tamper)
    {
        Assert.True(k >= 0);
        for (int i = 0; i < k; i++)
        {
            var m = stream[i];
            if (m.Seat == PvpSeat.Host) { Assert.NotNull(host.ApplyLocalMove(m.Move)); Assert.Equal(PvpApplyResult.Ok, guest.ApplyRemote(m)); }
            else { Assert.NotNull(guest.ApplyLocalMove(m.Move)); Assert.Equal(PvpApplyResult.Ok, host.ApplyRemote(m)); }
        }
        return guest.ApplyRemote(tamper(stream[k]));
    }

    [Fact]
    public void ShortLockstepSample() => Run(24, 0);

    // The newest archetypes in live v2.0 (WCG-382–402: 腐根/劇毒, 再起戰吼, 聖泉) get dedicated decks so every kept new card is exercised in lockstep. Not tuned or deck-legal.
    private static List<Deck> NewCardDecks()
    {
        var decks = new List<Deck>();
        foreach (var (lo, hi) in new[] { (382, 390), (391, 399), (400, 408), (409, 417), (418, 426) })
        {
            var ids = Enumerable.Range(lo, hi - lo + 1).Select(n => $"WCG-{n:000}").Where(id => Cards.GetCard(id) != null).ToList();
            if (ids.Count == 0) continue;
            var will = Cards.GetCard(ids[0])!.Will;
            var filler = (Pool.FirstOrDefault(e => e.Deck.MainWill == will) ?? Pool[0]).Deck.CardIds;
            var list = ids.SelectMany(id => Enumerable.Repeat(id, 4)).Take(36).ToList();
            list.AddRange(filler.Take(50 - list.Count));
            decks.Add(new Deck { Id = $"new-{lo}", Name = $"new {lo}-{hi}", MainWill = will, CardIds = list });
        }
        return decks;
    }

    [Fact, Trait("Category", "Slow")]
    public void NewestCardsStayInLockstep()
    {
        var decks = NewCardDecks(); int moves = 0;
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
        var jobs = (from a in Enumerable.Range(0, decks.Count) from b in Enumerable.Range(0, decks.Count) from s in Enumerable.Range(0, 4) select (a, b, s)).ToArray();
        var results = new Outcome?[jobs.Length];
        Parallel.For(0, jobs.Length, i =>
        {
            var (a, b, s) = jobs[i];
            try { results[i] = Play(50_000 + i * 31, s % 2 == 0, decks[a], decks[b], 2 + s); }
            catch (Exception ex) { errors.Add($"{decks[a].Name} vs {decks[b].Name} #{s}: {ex.Message}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors.Take(5)));
        foreach (var o in results) { Assert.True(o!.Over); moves += o.Moves; }
        Console.WriteLine($"PvP lockstep, newest cards: {jobs.Length} matches, {moves} moves");
        Assert.True(moves > 0);
    }

    [Fact, Trait("Category", "Slow")]
    public void ManyFullMatchesStayInLockstep() => Run(Pool.Count >= 300 ? 300 : Pool.Count * 2, 1000);

    private static void Run(int matches, int seedBase)
    {
        int over = 0, moves = 0, fallbacks = 0, revealed = 0, shuffled = 0;
        var kinds = new HashSet<string>();
        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
        var results = new Outcome?[matches];
        Parallel.For(0, matches, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) }, i =>
        {
            var a = Pool[(i * 7) % Pool.Count].Deck; var b = Pool[(i * 13 + 5) % Pool.Count].Deck;
            try { results[i] = Play(seedBase + i * 7919 + 3, i % 2 == 0, a, b, i % 6); }
            catch (Exception ex) { errors.Add($"match {i} ({a.Name} vs {b.Name}): {ex.Message}"); }
        });
        Assert.True(errors.IsEmpty, string.Join("\n", errors.Take(5)));
        foreach (var o in results) { over += o!.Over ? 1 : 0; moves += o.Moves; fallbacks += o.Fallbacks; foreach (var m in o.Stream) kinds.Add(m.Move.Type); revealed += o.Reveal ? 1 : 0; shuffled += o.Shuffle ? 1 : 0; }
        Console.WriteLine($"PvP lockstep: {matches} matches, {over} finished, {moves} moves, {revealed} with a reveal, {shuffled} with a reshuffle/search/random discard, {fallbacks} uncapturable AI steps, kinds: {string.Join(",", kinds.Order())}");
        Assert.Equal(matches, over + fallbacks);
        Assert.True(fallbacks * 20 <= matches, $"{fallbacks} matches stopped at an uncapturable AI step");
    }
}
