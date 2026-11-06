using Xunit;

namespace WcgTests;

// UI audit items 4–6: source checks for the behaviour verified in the browser.
public class UiAuditFollowupTests
{
    private static string Src(string path) =>
        File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")), path));

    [Fact]
    public void Battle_board_does_not_scroll_and_dialog_shades_cover_the_viewport()
    {
        var css = Src("WcgWeb/Components/Battle/BattleBoard.razor.css");
        Assert.Contains(".v06-board{overflow:clip}", css);
        Assert.Contains(".modal-shade{position:fixed;inset:0;z-index:1050}", css);
        Assert.Contains(".history-dialog>.close-dialog{position:sticky;top:0", css);
    }

    [Fact]
    public void Escape_closes_battle_dialogs_but_not_effect_choices()
    {
        var board = Src("WcgWeb/Components/Battle/BattleBoard.razor");
        Assert.Contains("[JSInvokable]public async Task CloseDialog()", board);
        Assert.DoesNotContain("Pending=null", board[board.IndexOf("CloseDialog()")..board.IndexOf("EndDrag()")]);
        Assert.Contains("call('CloseDialog')", Src("WcgWeb/wwwroot/battle/fan-drag.js"));
    }

    [Theory]
    [InlineData(9, 5)]
    [InlineData(7, 7)]
    [InlineData(8, 4)]
    [InlineData(15, 5)]
    [InlineData(0, 1)]
    public void Energy_pips_wrap_into_even_rows(int n, int cols)
    {
        int rows = n <= 0 ? 1 : (n + 6) / 7;
        Assert.Equal(cols, n <= 0 ? 1 : (n + rows - 1) / rows);
        Assert.Contains("private static int PipColumns(int n){if(n<=0)return 1;var rows=(n+6)/7;return (n+rows-1)/rows;}", Src("WcgWeb/Components/Battle/BattleBoard.razor"));
    }

    [Fact]
    public void Switch_state_reads_off_not_close_in_english()
    {
        Assert.Contains("IsEnglish ? \"Off\" : \"關閉\"", Src("WcgWeb/Services/Localization/Localizer.cs"));
        foreach (var page in new[] { "WcgWeb/Components/Pages/Settings.razor", "SoulOath.Static/Components/Pages/Settings.razor", "WcgWeb/Components/Common/MotionSettings.razor" })
            Assert.DoesNotContain("? \"開啟\" : \"關閉\"]", Src(page));
    }

    [Fact]
    public void Deck_builder_marks_unsaved_copies_and_filters_official_decks()
    {
        var page = Src("WcgWeb/Components/Pages/DeckBuilder.razor");
        Assert.Contains("unsaved-badge", page);
        Assert.Contains("copy-source-filters", page);
        Assert.Contains("aria-label=\"@L[\"關閉\"]\" @onclick=\"CloseDeckModal\"", page);
        Assert.Contains("L.F(\"{0}（副本）\"", Src("WcgWeb/Components/Pages/DeckBuilder.razor.cs"));
        var en = Src("WcgWeb/i18n/en.json");
        foreach (var key in new[] { "未儲存", "全部意志", "單雙色皆可", "顯示 {0} / {1} 套", "搜尋牌組名稱或說明", "{0}（副本）" })
            Assert.Contains($"\"{key}\":", en);
    }
}
