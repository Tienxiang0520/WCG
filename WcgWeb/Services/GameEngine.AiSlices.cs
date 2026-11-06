namespace WcgWeb.Services;

// One AI step split into slices so a single-threaded browser can render while the computer thinks.
// ExecuteAiStep runs the same slice iterator in one go, so a sliced step makes exactly the same decision and
// leaves exactly the same state: between slices the AI has only been working on simulation clones.
// Callers must keep every other mutation out until the step completes (BattleCoordinator marks itself busy).
public partial class GameEngine
{
    internal sealed class Out<T> { public T Value = default!; }
    private static void RunSlices(IEnumerable<int> slices) { foreach (var _ in slices) { } }

    // Tests: AI steps started on this thread (journaled computer moves must replay without any).
    [ThreadStatic] internal static int AiStepsStarted;
    private IEnumerable<int> AiStepSlices(Out<bool> result)
    {
        AiStepsStarted++;
        if (AiLevel >= 0 && AiRevision >= 7) { foreach (var slice in ExecuteV7AiSlices(result)) yield return slice; yield break; }
        result.Value = AiLevel < 0 ? ExecuteV06Ai() : ExecuteTieredAi();
    }

    internal AiStepRun BeginAiStep() => new(this);

    // Advance runs one slice under the engine lock and returns true once the step is done (Result is then
    // what ExecuteAiStep would have returned). A started run must be finished: call Finish when abandoning it.
    internal sealed class AiStepRun(GameEngine engine)
    {
        private IEnumerator<int>? slices;
        private readonly Out<bool> result = new();
        private bool publish, done;
        public bool IsDone => done;
        public bool Result => result.Value;
        public bool Advance()
        {
            if (done) return true;
            lock (engine._gate)
            {
                try
                {
                    if (slices == null)
                    {
                        publish = engine._mutationDepth++ == 0;
                        if (publish) engine.BeginPublishedChange();
                        slices = engine.AiStepSlices(result).GetEnumerator();
                    }
                    if (slices.MoveNext()) return false;
                    done = true;
                    if (publish) engine.EndPublishedChange(result.Value);
                }
                catch { done = true; throw; }
                finally { if (done) { engine._mutationDepth--; slices?.Dispose(); } }
            }
            if (publish) engine.OnStateChanged?.Invoke();
            return true;
        }
        public void Finish() { while (!Advance()) { } }
    }
}
