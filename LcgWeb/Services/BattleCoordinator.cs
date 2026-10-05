using Microsoft.Extensions.Logging;
using LcgWeb.Models.Battle;

namespace LcgWeb.Services;

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
    public async Task DriveAsync(Guid token, Func<BattleResponse, Task> publish, CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(Fast ? 100 : 450, cancellation);
                BattleResponse? response = null;
                lock (gate)
                {
                    if (presenter != token) return;
                    if (pending != null && Now >= deadline)
                    {
                        // Resync without replaying an unacknowledged animation batch.
                        pending = null;
                        response = new(true, "resync", "動畫已同步。", Guid.NewGuid(), bridge.Snapshot(), []);
                    }
                    else if (!IsBusy && Error == "" && engine.DecisionPlayerId == "computer" && !engine.IsOver)
                    {
                        executing = true;
                        try { response = bridge.AiStep(); Track(response); }
                        catch (Exception ex) { logger.LogError(ex, "Battle AI failed"); Error = "電腦結算發生錯誤，請返回房間重開。";
                            response = new(false, "ai-error", Error, Guid.NewGuid(), bridge.Snapshot(), []); }
                        finally { executing = false; }
                    }
                }
                if (response != null) await publish(response);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
    public void Dispose() { lock (gate) { presenter = null; pending = null; } }
}
