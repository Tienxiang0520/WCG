namespace WcgTests;

// The static site's Settings page is its own component (scoped CSS is matched to the component's own file path,
// so it cannot link the server version's Settings.razor.css). Keep the two style files identical.
public sealed class SharedStyleTests
{
    [Fact]
    public void StaticSettingsStylesMatchTheServerVersion()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        Assert.Equal(File.ReadAllText(Path.Combine(root, "WcgWeb/Components/Pages/Settings.razor.css")),
            File.ReadAllText(Path.Combine(root, "SoulOath.Static/Components/Pages/Settings.razor.css")));
    }
}
