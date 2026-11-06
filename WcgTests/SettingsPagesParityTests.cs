using System.Text.RegularExpressions;
using Xunit;

namespace WcgTests;

// The deployed site (SoulOath.Static) has its own Settings.razor twin; #104 added 動畫 only to the WcgWeb one.
public class SettingsPagesParityTests
{
    private static string Src(string path) =>
        File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")), path));

    private static List<string> Sections(string page) =>
        Regex.Matches(page, @"<(\w+Settings) />|aria-labelledby=""([\w-]+-settings)""")
             .Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();

    [Fact]
    public void Static_and_server_settings_pages_list_the_same_sections_in_order()
    {
        var web = Sections(Src("WcgWeb/Components/Pages/Settings.razor"));
        var stat = Sections(Src("SoulOath.Static/Components/Pages/Settings.razor"));
        Assert.Contains("MotionSettings", web);
        Assert.Equal(web, stat);
    }

    [Fact]
    public void Tapped_monsters_get_a_badge_without_breaking_the_empty_slot_else_branch()
    {
        var board = Src("WcgWeb/Components/Battle/BattleBoard.razor");
        Assert.Contains("class=\"tapped-badge\"", board);
        // The badge must not sit between `@if(m==null){empty}` and its `else`, or every untapped slot renders the monster branch.
        Assert.Matches(@"@if\(m==null\)\{<div class=""empty"">＋</div>\}\s*else", board);
        Assert.Contains(".v06-slot.tapped .field-card{filter:", Src("WcgWeb/Components/Battle/BattleBoard.razor.css"));
    }

    [Fact]
    public void Open_phone_menu_is_lifted_above_the_deck_builder_overlays()
    {
        Assert.Contains(".sidebar:has(.nav-scrollable.mobile-open) { z-index:1150; }", Src("WcgWeb/wwwroot/wcg.css"));
    }
}
