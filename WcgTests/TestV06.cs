using WcgWeb.Services;
namespace WcgTests;
internal static class TestV06
{
    internal static void FinishPlacement(GameEngine e)
    {if(e.CurrentPendingChoice is {} p&&p.Title.Contains("固定進場"))Assert.True(e.SelectChoice(p.Options[0]));}
}
