namespace WcgTests;

// Deck library follows Hearthstone / Master Duel: the list holds only the player's own decks;
// 官方預組 (preset + reference decks, one source) appear only after 「＋ 新增牌組」.
public class DeckLibraryLayoutTests
{
    private static string Src(string rel)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        return File.ReadAllText(Path.Combine(root, rel));
    }

    [Fact]
    public void OwnDeckListDoesNotShowCopySources()
    {
        var src = Src("WcgWeb/Components/Pages/DeckBuilder.razor");
        var list = src[src.IndexOf("@if (NewDeckSource == null)")..src.IndexOf("else if (NewDeckSource == \"\")")];
        Assert.Contains("DeckSvc.GetCustomDecks()", list);
        Assert.Contains("＋ 新增牌組", list);
        Assert.Contains("CloneOwnDeck(cd)", list);
        Assert.Contains("LoadCustomDeck(cd)", list);
        Assert.Contains("DeleteCustomDeck(cd.Id", list);
        Assert.DoesNotContain("PresetDecks", list);
        Assert.DoesNotContain("GetReferenceDecks", list);
    }

    [Fact]
    public void NewDeckChooserOffersBlankAndOfficialDecks()
    {
        var src = Src("WcgWeb/Components/Pages/DeckBuilder.razor");
        var chooser = src[src.IndexOf("else if (NewDeckSource == \"\")")..];
        Assert.Contains("CreateNewDeck()", chooser);
        Assert.Contains("NewDeckSource = \"official\"", chooser);
        Assert.Contains("CardDb.PresetDecks.Concat(DeckSvc.GetReferenceDecks())", chooser);
        Assert.DoesNotContain("從參考牌組複製", chooser);
        Assert.Contains("ClonePreset(p)", chooser);
    }

    [Fact]
    public void CopyingOwnDeckUsesNewId()
    {
        var cs = Src("WcgWeb/Components/Pages/DeckBuilder.razor.cs");
        var body = cs[cs.IndexOf("private void CloneOwnDeck")..cs.IndexOf("private void LoadCustomDeck")];
        Assert.Contains("Guid.NewGuid()", body);
        Assert.Contains("new List<string>(own.CardIds)", body);
    }
}
