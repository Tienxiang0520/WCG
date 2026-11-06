using System.Security.Cryptography;
using System.Text;
using WcgWeb.Models;
using WcgWeb.Models.Ranked;

namespace WcgWeb.Services.Pvp;

// Peer-to-peer casual PvP, lockstep (PR 1: engine side only, no transport or UI).
// Both browsers run the same engine from the same handshake (seed, first player, both decks, rules fingerprint)
// and exchange only moves. Both engines are canonical: the host always sits in the engine's Player seat and the
// guest in its Computer seat, so the two engines are the very same state, never a swapped copy. Each client then
// reads it through a SeatView ("me" / "them"), so the guest still sees itself at the bottom of the board.
// Moves use the journaled computer-move format (RankedAction "aic:<kind>", GameEngine.AiCapture.cs) for either seat:
// hand positions are the acting seat's hand, unit positions and hero sides are absolute, so both sides decode alike.
// Neither seat is AI-driven (IsAi = false), so ranked AI never runs in a PvP engine. Casual only: never ranked.
public enum PvpSeat { Host = 0, Guest = 1 }

// The handshake both peers must agree on before the first move. Seed and HostFirst come from the host; decks are
// deck codes (DeckCode.cs) so each side can validate the other's list; Rules is the catalog fingerprint (the same
// value ranked uses for its era check) and Build the app version; StartRules is RankedRules.CurrentStartRules.
public sealed record PvpHandshake(int Version, string Build, string Rules, int StartRules, int Seed, bool HostFirst, string HostDeck, string GuestDeck)
{
    public const int CurrentVersion = 1;
    // Both peers compare this; any difference (app build, card catalog, start rule, decks, seed) means no match.
    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("\n", Version, Build, Rules, StartRules, Seed, HostFirst, HostDeck, GuestDeck))))[..32];
}

// One move on the wire. Seq starts at 1 and counts every move of both seats; StateHash is GameEngine.StateHash()
// after the move was applied by its sender, so the receiver detects a desync on the very move that caused it.
public sealed record PvpMove(long Seq, PvpSeat Seat, RankedAction Move, string StateHash);

// The transport contract the WebRTC data channel (later PR) implements. Delivery must be ordered and reliable.
public interface IPvpMoveSink { void Send(PvpMove move); }
public interface IPvpMoveSource { event Action<PvpMove>? Received; }

public enum PvpApplyResult { Ok, Desync, OutOfOrder, WrongSeat, Rejected }

// The client's view of the canonical engine: Me is the local seat, Them the other one. HeroSide converts a
// view-relative hero (mine/theirs) to the move format's absolute side (2 = engine Player, 3 = engine Computer).
public sealed class SeatView(GameEngine engine, PvpSeat local)
{
    public PvpSeat Local => local;
    public PlayerState Me => SeatOf(engine, local);
    public PlayerState Them => SeatOf(engine, local == PvpSeat.Host ? PvpSeat.Guest : PvpSeat.Host);
    public bool IsMyDecision => engine.DecisionPlayerId == Me.Id && !engine.IsOver;
    public int HeroSide(bool mine) => (mine ? Me : Them) == engine.Player ? 2 : 3;
    public static PlayerState SeatOf(GameEngine engine, PvpSeat seat) => seat == PvpSeat.Host ? engine.Player : engine.Computer;
    public static PvpSeat? SeatOfId(GameEngine engine, string id) => id == engine.Player.Id ? PvpSeat.Host : id == engine.Computer.Id ? PvpSeat.Guest : null;
}

// One client's lockstep session: applies local moves (captured into the wire format and sent) and remote moves
// (validated: sequence, seat to move, success, post-move hash). After a desync or a rejected move the session stops.
public sealed class PvpLockstep
{
    public GameEngine Engine { get; }
    public SeatView View { get; }
    public PvpHandshake Handshake { get; }
    public long LastSeq { get; private set; }
    public string Failure { get; private set; } = "";
    public bool Broken => Failure != "";
    private readonly IPvpMoveSink? sink;

    public PvpLockstep(CardDatabase catalog, PvpHandshake handshake, Deck hostDeck, Deck guestDeck, PvpSeat local, IPvpMoveSink? sink = null, IPvpMoveSource? source = null)
    {
        Handshake = handshake; this.sink = sink;
        Engine = new GameEngine(catalog, new Random(handshake.Seed));
        Engine.SetReplaySeed(handshake.Seed);
        Engine.EqualStartingLife = handshake.StartRules < 1; Engine.SecondPlayerCoin = handshake.StartRules >= 2;
        Engine.StartGame(hostDeck, guestDeck, handshake.HostFirst);
        Engine.Player.IsAi = false; Engine.Computer.IsAi = false;
        View = new SeatView(Engine, local);
        if (source != null) source.Received += m => ApplyRemote(m);
    }

    // The seat that must act next, or null when the match is over.
    public PvpSeat? ToMove => Engine.IsOver ? null : SeatView.SeatOfId(Engine, Engine.DecisionPlayerId);

    // Runs one local command (any engine primitive for the local seat: PlayEnergy, Attack, SelectChoice, EndTurn...)
    // and returns the move to send, or null when it failed or did not make exactly one locatable engine call.
    public PvpMove? ApplyLocal(Func<GameEngine, bool> command) => ApplyAs(View.Local, command, 0, send: true);
    // A local move already in the wire format (e.g. replaying a stream): applied and sent like ApplyLocal.
    public PvpMove? ApplyLocalMove(RankedAction move) => ApplyAs(View.Local, g => g.ApplySeatCall(SeatView.SeatOf(g, View.Local), move), 1, send: true);

    // Tests and tools: capture a move for either seat. depth 0: `command` is one engine primitive; depth 1: it is
    // one wrapping change (ExecuteAiStep, ApplySeatCall) whose single top-level primitive is the move.
    internal PvpMove? ApplyAs(PvpSeat seat, Func<GameEngine, bool> command, int depth, bool send)
    {
        if (Broken || ToMove != seat) return null;
        Engine.BeginSeatCapture(SeatView.SeatOf(Engine, seat), depth);
        bool ok; RankedAction? move;
        try { ok = command(Engine); } finally { move = Engine.EndAiCapture(); }
        if (!ok) return null;
        if (move == null) { Failure = "本機操作無法轉成對戰指令。"; return null; }
        var wire = new PvpMove(++LastSeq, seat, move, Engine.StateHash());
        if (send) sink?.Send(wire);
        return wire;
    }

    public PvpApplyResult ApplyRemote(PvpMove m)
    {
        if (Broken) return PvpApplyResult.Rejected;
        if (m.Seq != LastSeq + 1) return Fail(PvpApplyResult.OutOfOrder, $"對戰指令順序錯誤（預期 {LastSeq + 1}，收到 {m.Seq}）。");
        if (ToMove != m.Seat || m.Seat == View.Local) return Fail(PvpApplyResult.WrongSeat, "收到不該由對手執行的指令。");
        bool ok;
        try { ok = Engine.ApplySeatCall(SeatView.SeatOf(Engine, m.Seat), m.Move); }
        catch (InvalidDataException ex) { return Fail(PvpApplyResult.Rejected, ex.Message); }
        if (!ok) return Fail(PvpApplyResult.Rejected, "對手的指令無法執行：" + Engine.LastError);
        LastSeq = m.Seq;
        if (Engine.StateHash() != m.StateHash) return Fail(PvpApplyResult.Desync, $"雙方對局狀態不一致（第 {m.Seq} 步）。");
        return PvpApplyResult.Ok;
    }
    private PvpApplyResult Fail(PvpApplyResult r, string why) { Failure = why; return r; }
}
