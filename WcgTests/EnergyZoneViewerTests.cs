using System.Text.RegularExpressions;

namespace WcgTests;

// The live battle board is BattleBoard.razor (practice, ranked, tutorial and the static site all use it).
// PR #67 only wired the tap-to-view energy panel into the old Phaser board, so tapping did nothing on the site.
public class EnergyZoneViewerTests
{
    private static string Board()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        return File.ReadAllText(Path.Combine(root, "WcgWeb/Components/Battle/BattleBoard.razor"));
    }

    [Fact]
    public void EnergyZoneOpensEnergyPanelOnClick()
    {
        var src = Board();
        var aside = Regex.Match(src, "<aside class=\"energy-zone[^>]*>").Value;
        Assert.Contains("@onclick=\"OpenEnergy\"", aside);
        Assert.Contains("@onkeydown=\"EnergyKey\"", aside);
        Assert.Contains("@if(ShowEnergy)", src);
        Assert.Contains("ShowEnergy=true", src);
    }

    [Fact]
    public void EnergyPanelShowsFacesOnlyWhenBridgeSendsThem()
    {
        var src = Board();
        var panel = src[src.IndexOf("@if(ShowEnergy)")..];
        panel = panel[..panel.IndexOf("</section></div>}")];
        // Faces come from BattleEnergy.Card, which BattleBridge fills for your own energy and public (face-up) cards only.
        Assert.Contains("e.Card is {} ec", panel);
        Assert.Contains("背面能量牌", panel);
        Assert.Contains("s.Player,s.Computer", panel);
    }

    [Fact]
    public void EnergyPanelDoesNotReuseGlobalEnergyBackClass()
    {
        // wcg.css styles .energy-back as a 34px (22px in-game) column tile; reusing it squeezed the label one character per line.
        var src = Board();
        var panel = src[src.IndexOf("@if(ShowEnergy)")..];
        panel = panel[..panel.IndexOf("</section></div>}")];
        Assert.DoesNotMatch("class=\"[^\"]*\\benergy-back\\b", panel);
        Assert.Contains("energy-dialog-back", panel);
    }

    private static string Panel(string src, string open)
    {
        var panel = src[src.IndexOf(open)..];
        return panel[..panel.IndexOf("</section></div>}")];
    }

    [Fact]
    public void ClickingCardInDialogsKeepsDialogOpen()
    {
        // Tapping an energy card used to run ShowEnergy=false before opening the detail, so the whole panel closed.
        var src = Board();
        foreach (var open in new[] { "@if(ShowEnergy)", "@if(ShowGrave)", "@if(ShowHistory)" })
        {
            var panel = Panel(src, open);
            Assert.Contains("@onclick=\"()=>Detail=", panel);
            Assert.DoesNotMatch(@"\{Show(Energy|Grave|History)=false;Detail=", panel);
        }
        // The detail dialog is rendered after the list dialogs so it stacks on top of them.
        Assert.True(src.IndexOf("@if(Detail is {} card)") > src.IndexOf("@if(ShowEnergy)"));
    }

    [Fact]
    public void LogAndGraveyardAreSeparateButtonsAndPanels()
    {
        var src = Board();
        Assert.DoesNotContain("紀錄／墓地", src);
        Assert.Contains("class=\"open-log\" @onclick=\"()=>ShowHistory=true\">@L[\"紀錄\"]", src);
        Assert.Contains("class=\"open-grave\" @onclick=\"()=>ShowGrave=true\">@L[\"墓地\"]", src);
        var grave = Panel(src, "@if(ShowGrave)");
        Assert.Contains("side.Graveyard", grave);
        Assert.DoesNotContain("History.Take", grave);
        var log = Panel(src, "@if(ShowHistory)");
        Assert.Contains("History.Take", log);
        Assert.DoesNotContain("side.Graveyard", log);
    }
}
