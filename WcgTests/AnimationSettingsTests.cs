using System.Text.RegularExpressions;
using WcgWeb.Services;
using Xunit;

namespace WcgTests;

// 動畫速度 slider and 關閉動畫 switch: one central multiplier shared by CSS, JS and Blazor delays.
[Collection("AnimationTiming")]
public class AnimationSettingsTests
{
    private static string Src(string path) =>
        File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")), path));

    [Fact]
    public void Timing_scales_by_speed_and_drops_to_floor_when_off()
    {
        try
        {
            AnimationTiming.Update(2, false);
            Assert.Equal(225, AnimationTiming.Scale(450, 30));
            AnimationTiming.Update(9, false);
            Assert.Equal(AnimationTiming.MaxSpeed, AnimationTiming.Speed);
            AnimationTiming.Update(0.25, false);
            Assert.Equal(1800, AnimationTiming.Scale(450));
            AnimationTiming.Update(1, true);
            Assert.Equal(30, AnimationTiming.Scale(450, 30));
            AnimationTiming.Update(double.NaN, false);
            Assert.Equal(AnimationTiming.DefaultSpeed, AnimationTiming.Speed);
        }
        finally { AnimationTiming.Update(1, false); }
    }

    [Fact]
    public void Settings_page_has_a_slider_not_preset_buttons_and_an_off_switch()
    {
        var page = Src("WcgWeb/Components/Pages/Settings.razor");
        Assert.Contains("<MotionSettings />", page);
        var c = Src("WcgWeb/Components/Common/MotionSettings.razor");
        Assert.Contains("type=\"range\"", c);
        Assert.Contains("@L[\"關閉動畫\"]", c);
        Assert.Contains("@L[\"動畫速度\"]", c);
        Assert.DoesNotContain("\"慢\"", c);
        Assert.Contains("saveAnimationSpeed", c);
        Assert.Contains("saveAnimationOff", c);
    }

    [Fact]
    public void Hard_coded_delays_route_through_the_central_multiplier()
    {
        Assert.Contains("AnimationTiming.Scale(Fast ? 100 : 450", Src("WcgWeb/Services/BattleCoordinator.cs"));
        Assert.Contains("AnimationTiming.Speed", Src("WcgWeb/Components/Ranked/RankedReplayViewer.razor"));
        Assert.Contains("motionScale()", Src("WcgWeb/wwwroot/battle/battle-feedback.js"));
        Assert.Contains("motionScale()", Src("WcgWeb/wwwroot/battle/fan-drag.js"));
        var fx = Src("WcgWeb/wwwroot/battle/battle-fx.js");
        Assert.Contains("duration: scaleMs(options?.duration ?? 0, env)", fx);
        // Every CSS animation/transition time in the battle board scales with --wcg-motion-scale.
        foreach (var css in new[] { "WcgWeb/Components/Battle/BattleBoard.razor.css", "WcgWeb/wwwroot/battle/battle.css" })
            foreach (Match m in Regex.Matches(Src(css), @"(?:animation|transition)(?:-duration|-delay)?\s*:([^;}]*)"))
                if (!m.Groups[1].Value.Contains(".01ms"))
                    Assert.DoesNotMatch(@"(?<![\w.(-])\d*\.?\d+m?s\b(?! \* var)", Regex.Replace(m.Groups[1].Value, @"var\(--order\) \* ", ""));
    }

    [Fact]
    public void Strings_are_translated()
    {
        var en = Src("WcgWeb/i18n/en.json");
        foreach (var key in new[] { "\"動畫\"", "\"關閉動畫\"", "\"動畫速度\"" }) Assert.Contains(key + ":", en);
    }
}
