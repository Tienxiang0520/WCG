using Microsoft.JSInterop;
using WcgWeb.Models;

namespace WcgWeb.Services.Pvp;

public enum PvpStage { Idle, Preparing, HostWaitingReply, GuestShowingReply, Connecting, Handshaking, Playing, Ended, Failed }

// 好友對戰 (PR 2): drives one WebRTC peer (wwwroot/p2p/rtc.js) through copy-paste signaling, the handshake and the
// lockstep match. It is the IPvpMoveSink / IPvpMoveSource of its PvpLockstep. Casual only: ranked is never touched.
public sealed class PvpSession : IPvpMoveSink, IPvpMoveSource, IAsyncDisposable
{
    public const int HeartbeatMs = 2000, TimeoutMs = 15000;
    private readonly IJSRuntime js;
    private readonly CardDatabase cards;
    private readonly Func<Func<Task>, Task> dispatch;
    private readonly string build;
    private IJSObjectReference? module;
    private DotNetObjectReference<PvpSession>? self;
    private int peer;
    private Timer? heartbeat;
    private DateTime lastReceived = DateTime.UtcNow;
    private string myNonce = "", theirCommit = "", theirNonce = "";
    private PvpWire? myHello, theirHello;
    private Deck? myDeck;

    public PvpSeat Seat { get; private set; }
    public PvpStage Stage { get; private set; }
    public string Code { get; private set; } = "";
    public string Error { get; private set; } = "";
    public string Connection { get; private set; } = "";
    public bool PeerSilent { get; private set; }
    public string Result { get; private set; } = "";
    public PvpLockstep? Match { get; private set; }
    public event Action? Changed;
    public event Action<PvpMove>? Received;

    public PvpSession(IJSRuntime js, CardDatabase cards, Func<Func<Task>, Task> dispatch, string? buildOverride = null)
    { this.js = js; this.cards = cards; this.dispatch = dispatch; build = PvpProtocol.BuildId + (string.IsNullOrEmpty(buildOverride) ? "" : "+" + buildOverride); }

    private async Task<IJSObjectReference> Rtc()
    {
        module ??= await js.InvokeAsync<IJSObjectReference>("import", "./p2p/rtc.js");
        if (peer == 0) { self ??= DotNetObjectReference.Create(this); peer = await module.InvokeAsync<int>("create", self); }
        return module;
    }

    private void Notify() => Changed?.Invoke();
    private void Fail(string error) { Error = error; Stage = PvpStage.Failed; StopHeartbeat(); Notify(); }

    public async Task CreateRoom(Deck deck)
    {
        Reset(PvpSeat.Host, deck);
        try
        {
            var rtc = await Rtc();
            var sdp = await rtc.InvokeAsync<string>("createOffer", peer);
            var signal = PvpSignalCode.Parse(sdp, PvpSignalCode.Kind.Offer, out var err);
            if (signal == null) { Fail(err); return; }
            Code = PvpSignalCode.Encode(signal); Stage = PvpStage.HostWaitingReply; Notify();
        }
        catch (JSException) { Fail("這個瀏覽器無法建立點對點連線。"); }
    }

    public async Task Join(string roomCode, Deck deck)
    {
        var offer = PvpSignalCode.Decode(roomCode, PvpSignalCode.Kind.Offer, out var err);
        if (offer == null) { Error = err; Notify(); return; }
        Reset(PvpSeat.Guest, deck);
        try
        {
            var rtc = await Rtc();
            var sdp = await rtc.InvokeAsync<string>("acceptOffer", peer, PvpSignalCode.Rebuild(offer));
            var signal = PvpSignalCode.Parse(sdp, PvpSignalCode.Kind.Answer, out err);
            if (signal == null) { Fail(err); return; }
            Code = PvpSignalCode.Encode(signal); Stage = PvpStage.GuestShowingReply; Notify();
        }
        catch (JSException) { Fail("房間代碼無法使用，可能已過期，請房主重新建立房間。"); }
    }

    public async Task AcceptReply(string replyCode)
    {
        if (Stage != PvpStage.HostWaitingReply || module == null) return;
        var answer = PvpSignalCode.Decode(replyCode, PvpSignalCode.Kind.Answer, out var err);
        if (answer == null) { Error = err; Notify(); return; }
        // Connecting before the call: the channel may open while acceptAnswer is still running.
        Error = ""; Stage = PvpStage.Connecting; Notify();
        try { await module.InvokeVoidAsync("acceptAnswer", peer, PvpSignalCode.Rebuild(answer)); }
        catch (JSException)
        {
            if (Stage == PvpStage.Connecting) Stage = PvpStage.HostWaitingReply;
            Error = "回覆代碼無法使用，可能不是這個房間的代碼，請重新建立房間。"; Notify();
        }
    }

    private void Reset(PvpSeat seat, Deck deck)
    {
        Seat = seat; myDeck = deck; Error = ""; Code = ""; Result = ""; Match = null; Stage = PvpStage.Preparing;
        myNonce = PvpProtocol.NewNonce(); theirCommit = theirNonce = ""; theirHello = null;
        myHello = new PvpWire
        {
            T = "hello", V = PvpProtocol.Version, Build = build, Rules = PvpProtocol.RulesFingerprint(cards),
            StartRules = Models.Ranked.RankedRules.CurrentStartRules, Deck = DeckCode.Encode(deck),
            Commit = seat == PvpSeat.Host ? PvpProtocol.Commit(myNonce) : null, Nonce = seat == PvpSeat.Guest ? myNonce : null
        };
        Notify();
    }

    // Messages are handled strictly one at a time: a handler awaits sends, and the next message must not start meanwhile.
    private readonly SemaphoreSlim gate = new(1, 1);
    private Func<Task> Serial(Func<Task> work) => async () => { await gate.WaitAsync(); try { await work(); } finally { gate.Release(); } };

    [JSInvokable] public Task OnRtcOpen() => dispatch(Serial(Opened));

    // The channel is open: start the heartbeat and send hello. A message may arrive before the open event is handled,
    // so OnRtcMessage calls this too; it runs once.
    private async Task Opened()
    {
        if (Stage is not (PvpStage.Connecting or PvpStage.GuestShowingReply)) return;
        Stage = PvpStage.Handshaking; Connection = "connected"; lastReceived = DateTime.UtcNow;
        heartbeat = new Timer(_ => _ = dispatch(Beat), null, HeartbeatMs, HeartbeatMs);
        await Send(myHello!); Notify();
    }

    [JSInvokable] public Task OnRtcState(string state) => dispatch(() =>
    {
        Connection = state;
        if (state is "failed" or "closed" && Stage is PvpStage.Connecting or PvpStage.Handshaking or PvpStage.Playing)
            Lost(Stage == PvpStage.Connecting ? "無法連線。雙方的網路可能需要中繼伺服器，目前不支援。" : "連線中斷，對局已結束。");
        Notify(); return Task.CompletedTask;
    });

    [JSInvokable] public Task OnRtcMessage(string text) => dispatch(Serial(async () =>
    {
        lastReceived = DateTime.UtcNow; PeerSilent = false;
        await Opened();
        var w = PvpProtocol.Deserialize(text);
        if (w == null || Stage is PvpStage.Ended or PvpStage.Failed) return;
        switch (w.T)
        {
            case "ping": break;
            case "hello": await OnHello(w); break;
            case "start": OnStart(w); break;
            case "move":
                var m = PvpProtocol.ToMove(w);
                if (Stage != PvpStage.Playing || Match == null || m == null) { await Abort("收到無效的對戰指令。"); break; }
                Received?.Invoke(m);
                if (Match.Broken) await Abort(Match.Failure); else CheckOver();
                break;
            case "surrender": End("對手投降，你獲勝了。"); break;
            case "error": Fail(w.Reason ?? "對方中止了連線。"); break;
            case "bye": if (Stage != PvpStage.Ended) Lost("對手已離開，對局已結束。"); break;
        }
        Notify();
    }));

    private async Task OnHello(PvpWire w)
    {
        if (Stage != PvpStage.Handshaking || theirHello != null) return;
        var err = PvpProtocol.CheckHello(myHello!, w, theyAreHost: Seat == PvpSeat.Guest);
        Deck? theirDeck = null;
        if (err == "") { theirDeck = PvpProtocol.DeckFromCode(cards, w.Deck, out var deckErr); if (theirDeck == null) err = "對方的牌組不合規則。"; }
        if (err != "") { await Abort(err); return; }
        theirHello = w;
        if (Seat == PvpSeat.Guest) { theirCommit = w.Commit!; return; }
        theirNonce = w.Nonce!;
        var shake = Shake();
        await Send(new PvpWire { T = "start", Nonce = myNonce, Fingerprint = shake.Fingerprint() });
        Begin(shake);
    }

    private void OnStart(PvpWire w)
    {
        if (Seat != PvpSeat.Guest || Stage != PvpStage.Handshaking || theirHello == null) return;
        if (!PvpProtocol.CheckReveal(theirCommit, w.Nonce)) { _ = Abort("對方的開局資料無法驗證。"); return; }
        theirNonce = w.Nonce!;
        var shake = Shake();
        if (shake.Fingerprint() != w.Fingerprint) { _ = Abort("雙方的開局資料不一致。"); return; }
        Begin(shake);
    }

    private PvpHandshake Shake()
    {
        var (hostNonce, guestNonce) = Seat == PvpSeat.Host ? (myNonce, theirNonce) : (theirNonce, myNonce);
        var (seed, hostFirst) = PvpProtocol.Derive(hostNonce, guestNonce);
        var (hostDeck, guestDeck) = Seat == PvpSeat.Host ? (myHello!.Deck!, theirHello!.Deck!) : (theirHello!.Deck!, myHello!.Deck!);
        return new PvpHandshake(PvpHandshake.CurrentVersion, build, myHello!.Rules!, myHello.StartRules, seed, hostFirst, hostDeck, guestDeck);
    }

    private void Begin(PvpHandshake shake)
    {
        var host = PvpProtocol.DeckFromCode(cards, shake.HostDeck, out _)!;
        var guest = PvpProtocol.DeckFromCode(cards, shake.GuestDeck, out _)!;
        Match = new PvpLockstep(cards, shake, host, guest, Seat, this, this);
        // Seat names stay the engine's own on both sides: they are part of the state hash, so a per-client "you" would desync.
        Stage = PvpStage.Playing; Notify();
    }

    // Runs one local engine command; false when it is not this seat's turn or the engine refused it.
    public bool Act(Func<GameEngine, bool> command)
    {
        if (Stage != PvpStage.Playing || Match == null) return false;
        var sent = Match.ApplyLocal(command);
        if (Match.Broken) _ = Abort(Match.Failure);
        else CheckOver();
        Notify();
        return sent != null;
    }

    private void CheckOver()
    {
        if (Match?.Engine.IsOver != true || Stage != PvpStage.Playing) return;
        End(Match.View.Me.HasLost ? "本局敗北。" : "本局獲勝！");
    }

    public async Task Surrender()
    {
        if (Stage != PvpStage.Playing) return;
        await Send(new PvpWire { T = "surrender" });
        End("你已投降，本局敗北。");
    }

    private void End(string result) { Result = result; Stage = PvpStage.Ended; StopHeartbeat(); Notify(); }
    private void Lost(string why) { if (Stage == PvpStage.Playing) End(why); else Fail(why); }
    private async Task Abort(string why) { await Send(new PvpWire { T = "error", Reason = why }); Fail(why); }

    private async Task Beat()
    {
        if (Stage is not (PvpStage.Handshaking or PvpStage.Playing)) return;
        await Send(new PvpWire { T = "ping" });
        var silent = (DateTime.UtcNow - lastReceived).TotalMilliseconds;
        if (silent > TimeoutMs) { Lost("對手已經 15 秒沒有回應，視為斷線，對局已結束。"); return; }
        var was = PeerSilent; PeerSilent = silent > HeartbeatMs * 2.5;
        if (was != PeerSilent) Notify();
    }

    private async Task Send(PvpWire w)
    {
        if (module == null || peer == 0) return;
        try { await module.InvokeAsync<bool>("send", peer, PvpProtocol.Serialize(w)); } catch (JSException) { } catch (JSDisconnectedException) { }
    }

    void IPvpMoveSink.Send(PvpMove move) => _ = Send(PvpProtocol.FromMove(move));

    private void StopHeartbeat() { heartbeat?.Dispose(); heartbeat = null; }

    public async Task Close()
    {
        StopHeartbeat();
        if (module != null && peer != 0)
        {
            if (Stage is PvpStage.Handshaking or PvpStage.Playing) await Send(new PvpWire { T = "bye" });
            try { await module.InvokeVoidAsync("close", peer); } catch (JSException) { } catch (JSDisconnectedException) { }
        }
        peer = 0;
    }

    public async ValueTask DisposeAsync()
    {
        await Close();
        if (module != null) { try { await module.DisposeAsync(); } catch (JSDisconnectedException) { } }
        self?.Dispose();
    }
}
