namespace WcgTests;

// Physical table: the opponent's battlefield cards face them (art/frame rotated 180°), numbers stay readable,
// a temporarily controlled monster reads as turned relative to its row, and ↑ always points at the other row.
public sealed class BattlefieldFacingTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact]
    public void OpponentFacingCardsRotateArtButNotTheirNumbers()
    {
        var css = Read("WcgWeb/Components/Battle/BattleBoard.razor.css");
        Assert.Contains(".field-card.enemy-card>.card-art-layer,.field-card.enemy-card>.field-card-back,.field-card.enemy-card>.taunt-frame{transform:rotate(180deg)}", css);
        Assert.DoesNotContain(".field-card.turned{transform", css); // the whole card (badges, lunge) no longer spins
        Assert.DoesNotContain("field-stats{transform", css);
        Assert.Contains(".field-card.upper-row:not(.enemy-card) .arrowmark.up{top:auto;bottom:-12px", css);
        Assert.Contains(".field-card.enemy-card:not(.upper-row) .arrowmark.up{top:-12px", css);
        Assert.DoesNotContain(".locked .field-card .arrowmark{", css.Replace(",.locked .field-card .arrowmark{", "{"));
        var razor = Read("WcgWeb/Components/Battle/BattleBoard.razor");
        Assert.Contains("@(ctrl?\"\":\"enemy-card\") @(mine?\"\":\"upper-row\")", razor);
        Assert.Contains("facing-away", Read("WcgWeb/Components/Ranked/RankedReplayViewer.razor"));
        Assert.Contains(".replay-slot.facing-away .replay-art { transform: rotate(180deg); }", Read("WcgWeb/Components/Ranked/RankedReplayViewer.razor.css"));
    }
}
