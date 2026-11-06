namespace WcgWeb.Services;

/// <summary>
/// C# mirror of the 動畫速度 / 關閉動畫 settings (stored by wwwroot/battle/battle-fx.js under
/// wcg.animSpeed and wcg.motion). The browser side owns the values; MainLayout, BattleBoard and the
/// settings page push them here so Blazor-timed delays use the same central multiplier as CSS and JS.
/// Purely presentational: no game rule, AI decision or replay record depends on it.
/// </summary>
public static class AnimationTiming
{
    public const double MinSpeed = 0.25, MaxSpeed = 3, DefaultSpeed = 1;
    public static double Speed { get; private set; } = DefaultSpeed;
    public static bool Off { get; private set; }

    public static void Update(double speed, bool off)
    {
        Speed = double.IsFinite(speed) ? Math.Clamp(speed, MinSpeed, MaxSpeed) : DefaultSpeed;
        Off = off;
    }

    /// <summary>Scales a hard-coded delay; when animations are off it drops to <paramref name="floorWhenOff"/>.</summary>
    public static int Scale(int milliseconds, int floorWhenOff = 0) =>
        Off ? floorWhenOff : Math.Max(0, (int)Math.Round(milliseconds / Speed));
}

/// <summary>Shape returned by battle-fx.js readAnimationSettings().</summary>
public sealed record AnimationSettingsState(bool Off, double Speed, string Setting);
