using LcgWeb.Models;
using LcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace LcgTests;
public class DeckStorageTests : IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"lcg-test-"+Guid.NewGuid());
    readonly CardDatabase db;
    readonly DeckService a,b;
    public DeckStorageTests()
    {
        Directory.CreateDirectory(Path.Combine(root,"Data"));
        File.Copy(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../LcgWeb/Data/cards.json")),Path.Combine(root,"Data/cards.json"));
        File.Copy(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../LcgWeb/Data/preset_decks.json")),Path.Combine(root,"Data/preset_decks.json"));
        var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(root);db=new(env.Object);a=new(db,env.Object);b=new(db,env.Object);
    }
    Deck NewDeck(string id) { var d=DeckService.Copy(db.PresetDecks[0]);d.Id=id;return d; }
    [Fact] public void TwoScopesPreserveIndependentSavesAndRejectStaleOverwrite()
    {
        var x=NewDeck("x");var y=NewDeck("y");a.GetCustomDecks();b.GetCustomDecks();a.SaveDeck(x);b.SaveDeck(y);Assert.Equal(2,a.GetCustomDecks().Count);Assert.Equal("狂怒",b.GetDeck("x")!.MainWill);
        var old=b.GetDeck("x")!;x.Name="updated";a.SaveDeck(x);Assert.Throws<InvalidOperationException>(()=>b.SaveDeck(old));Assert.Throws<InvalidOperationException>(()=>b.DeleteDeck(old.Id,old.Version));Assert.Equal("updated",b.GetDeck("x")!.Name);
        b.DeleteDeck(x.Id,x.Version);Assert.Single(a.GetCustomDecks());
    }
    [Fact] public void ReadReturnsCopiesAndMalformedFileCannotBeOverwritten()
    {
        var x=NewDeck("x");a.SaveDeck(x);a.GetDeck("x")!.CardIds.Clear();Assert.Equal(50,a.GetDeck("x")!.CardIds.Count);
        var path=Path.Combine(root,"Data/custom_decks.json");File.WriteAllText(path,"broken data");Assert.Empty(a.GetCustomDecks());Assert.NotNull(a.LastStorageError);Assert.Throws<InvalidOperationException>(()=>a.SaveDeck(NewDeck("y")));Assert.Equal("broken data",File.ReadAllText(path));
    }
    [Fact] public void WriteFailureDoesNotAdvanceVersionOrClaimSaved()
    {
        Directory.CreateDirectory(Path.Combine(root,"Data/custom_decks.json"));var x=NewDeck("x");
        Assert.ThrowsAny<IOException>(()=>a.SaveDeck(x));Assert.Equal(0,x.Version);Assert.Empty(Directory.GetFiles(Path.Combine(root,"Data"),".decks-*.tmp"));
    }
    public void Dispose()=>Directory.Delete(root,true);
}
