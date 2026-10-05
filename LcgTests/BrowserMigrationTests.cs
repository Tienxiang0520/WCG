using System.Text.Json;
using LcgWeb.Models;
using LcgWeb.Services;
namespace LcgTests;
public sealed class BrowserMigrationTests
{
    private readonly CardDatabase cards;
    private readonly RankedDecks opponents;
    public BrowserMigrationTests()
    {
        var data=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../LcgWeb/Data"));
        cards=new(File.ReadAllText(Path.Combine(data,"cards.json")),File.ReadAllText(Path.Combine(data,"preset_decks.json")));
        opponents=new(cards,File.ReadAllText(Path.Combine(data,"ranked_decks.json")));
    }
    private static string Backup(params (string Key,string Value)[] entries)=>JsonSerializer.Serialize(new {format="soul-oath-local",version=1,data=entries.ToDictionary(e=>e.Key,e=>e.Value),preferences=new {} });
    [Fact] public void ValidCustomDeckAndFreshRankedProfileCanBeImported()
    {
        var d=DeckService.Copy(cards.PresetDecks[0]);d.Id="local-copy";
        PlayerBackupValidator.Validate(Backup(("decks",JsonSerializer.Serialize(new[]{d})),("ranked","{\"Version\":1,\"Season\":\"2026-10\",\"Stars\":2}")),cards,opponents);
    }
    [Fact] public void MalformedDecksAndInvalidDeckRulesAreRejectedBeforeImport()
    {
        Assert.Throws<InvalidDataException>(()=>PlayerBackupValidator.Validate(Backup(("decks","broken")),cards,opponents));
        var d=DeckService.Copy(cards.PresetDecks[0]);d.Id="bad";d.CardIds.Clear();
        Assert.Throws<InvalidDataException>(()=>PlayerBackupValidator.Validate(Backup(("decks",JsonSerializer.Serialize(new[]{d}))),cards,opponents));
    }
    [Fact] public void BackupCannotReplacePresetDeckIdentity()
    {
        Assert.Throws<InvalidDataException>(()=>PlayerBackupValidator.Validate(Backup(("decks",JsonSerializer.Serialize(new[]{cards.PresetDecks[0]}))),cards,opponents));
    }
    [Fact] public void InvalidRankedProgressIsRejectedBeforeImport()
    {
        Assert.Throws<InvalidDataException>(()=>PlayerBackupValidator.Validate(Backup(("ranked","{\"Version\":1,\"Season\":\"2026-13\",\"Stars\":2}")),cards,opponents));
    }
}
