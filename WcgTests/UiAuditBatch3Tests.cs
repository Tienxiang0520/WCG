using Xunit;

namespace WcgTests;

// UI audit batch 3: source checks for the behaviour verified in the browser.
public class UiAuditBatch3Tests
{
    private static string Src(string path) =>
        File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")), path));

    [Fact]
    public void Hand_shows_full_card_faces()
        => Assert.Contains("<CardFace Battle=\"h.Card\" Class=\"hand-face\"", Src("WcgWeb/Components/Battle/BattleBoard.razor"));

    [Fact]
    public void Surrender_asks_for_confirmation_and_game_over_hides_hand()
    {
        var board = Src("WcgWeb/Components/Battle/BattleBoard.razor");
        Assert.Contains("ConfirmSurrender=true", board);
        Assert.Contains("投降後這局立即判定為敗北，無法復原。", board);
        Assert.Contains("對局結束", board);
        Assert.Contains(".v06-board.is-over .v06-hand,.v06-board.is-over .hand-hint{visibility:hidden", Src("WcgWeb/Components/Battle/BattleBoard.razor.css"));
    }

    [Fact]
    public void Choice_dialogs_wrap_without_inner_scroll()
        => Assert.Contains(".v06-dialog>.choice-cards{", Src("WcgWeb/Components/Battle/BattleBoard.razor.css"));

    [Fact]
    public void Escape_closes_page_dialogs_and_dual_chip_is_locked_for_single_colour()
    {
        Assert.Contains("data-esc-close", Src("WcgWeb/Components/Pages/DeckBuilder.razor"));
        Assert.Contains("data-esc-close", Src("WcgWeb/Components/Common/AvatarBadge.razor"));
        Assert.Contains("__wcgEscClose", Src("WcgWeb/wwwroot/sidebar.js"));
        Assert.Contains("切換雙色牌組後可用", Src("WcgWeb/Components/Pages/DeckBuilder.razor"));
    }

    [Fact]
    public void New_strings_are_translated()
    {
        var en = Src("WcgWeb/i18n/en.json");
        foreach (var k in new[] { "確認投降", "繼續對戰", "切換雙色牌組後可用", "對局結束" })
            Assert.Contains($"\"{k}\"", en);
        Assert.Contains("拖到戰場中央", Src("WcgWeb/Services/Tutorial/TutorialLessons.cs"));
    }
}
