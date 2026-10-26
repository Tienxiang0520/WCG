using Microsoft.Extensions.Logging;
using WcgWeb.Models.Battle;

namespace WcgWeb.Services;

// One driver per active presenter, shared by legacy and Phaser pages in this circuit.
public sealed class BattleCoordinator(BattleBridge bridge, GameEngine engine, ILogger<BattleCoordinator> logger, TimeProvider? clock = null) : IDisposable
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    private readonly object gate = new();
    private Guid? presenter;
    private BattleResponse? pending;
    private DateTime deadline;
    private bool executing;
    public bool Fast { get; set; }
    public bool AiPaused { get; set; }
    public string Error { get; private set; } = "";
    public Guid Attach() { lock (gate) { presenter = Guid.NewGuid(); pending = null; Error = ""; return presenter.Value; } }
    public void Detach(Guid token) { lock (gate) { if (presenter == token) { presenter = null; pending = null; } } }
    public bool IsBusy { get { lock (gate) return pending != null && Now < deadline; } }
    public BattleResponse Submit(Guid token, BattleCommand command)
    {
        lock (gate)
        {
            if (presenter != token || executing || IsBusy && command.Type is not ("reset" or "start" or "surrender"))
                return new(false, "busy", "上一個動作仍在播放，請稍候。", Guid.NewGuid(), bridge.Snapshot(), []);
            var response = bridge.Submit(command);
            if (response.Success && command.Type is "start" or "reset") Error = "";
            Track(response); return response;
        }
    }
    private void Track(BattleResponse response)
    {
        if (response.Success) { pending = response; deadline = Now.AddSeconds(Math.Clamp(2 + response.Events.Length * .6, 8, 35)); }
    }
    public bool Acknowledge(Guid token, Guid match, long revision, Guid batch)
    {
        lock (gate)
        {
            if (presenter != token || pending == null || pending.BatchId != batch ||
                pending.State.MatchId != match || pending.State.Revision != revision) return false;
            pending = null; return true;
        }
    }
    public BattleResponse Resync(Guid token)
    {
        lock (gate)
        {
            if (presenter == token) pending = null;
            return new(true, "sync", "已同步目前對局。", Guid.NewGuid(), bridge.Snapshot(), []);
        }
    }
    public BattleResponse StepAi(Guid token)
    {
        lock(gate)
        {
            if(presenter != token || executing || IsBusy || !AiPaused || engine.DecisionPlayerId != "computer" || engine.IsOver)
                return new(false,"idle","請先暫停電腦，並等待電腦的行動或效果選擇。",Guid.NewGuid(),bridge.Snapshot(),[]);
            var response=bridge.AiStep(); Track(response); return response;
        }
    }
    public async Task DriveAsync(Guid token, Func<BattleResponse, Task> publish, CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                // The computer's pacing follows 動畫速度 / 關閉動畫 (AnimationTiming); 30 ms keeps the page responsive when off.
                await Task.Delay(AnimationTiming.Scale(Fast ? 100 : 450, floorWhenOff: 30), cancellation);
                BattleResponse? response = null; bool think = false;
                lock (gate)
                {
                    if (presenter != token) return;
                    if (pending != null && Now >= deadline)
                    {
                        // Resync without replaying an unacknowledged animation batch.
                        pending = null;
                        response = new(true, "resync", "動畫已同步。", Guid.NewGuid(), bridge.Snapshot(), []);
                    }
                    else if (!AiPaused && !IsBusy && Error == "" && engine.DecisionPlayerId == "computer" && !engine.IsOver)
                    {
                        executing = true; think = true;
                    }
                }
                // The step runs outside the lock and pauses between search slices so the page keeps rendering while the
                // computer thinks; `executing` keeps every other command out until it is done. Same decision as AiStep.
                if (think)
                {
                    try { response = await bridge.AiStepAsync(PauseEvery(SliceMilliseconds)); lock (gate) Track(response); }
                    catch (Exception ex) { logger.LogError(ex, "Battle AI failed"); lock (gate) { Error = "電腦結算發生錯誤，請返回房間重開。";
                        response = new(false, "ai-error", Error, Guid.NewGuid(), bridge.Snapshot(), []); } }
                    finally { lock (gate) executing = false; }
                }
                if (response != null) await publish(response);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
    // Longest stretch of AI work between two chances for the browser to render (WebAssembly runs on one thread).
    internal static int SliceMilliseconds { get; set; } = 30;
    internal static Func<ValueTask> PauseEvery(int milliseconds)
    {
        var since = System.Diagnostics.Stopwatch.StartNew();
        return async () =>
        {
            if (since.ElapsedMilliseconds < milliseconds) return;
            await Task.Delay(1); // a timer, not Task.Yield: the browser only paints between macrotasks
            since.Restart();
        };
    }
    public void Dispose() { lock (gate) { presenter = null; pending = null; } }
}
