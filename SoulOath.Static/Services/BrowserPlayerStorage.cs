using Microsoft.JSInterop;
namespace LcgWeb.Services;

public sealed class BrowserPlayerStorage(IJSRuntime runtime) : IPlayerStorage
{
    public object Gate { get; } = new();
    private IJSInProcessRuntime JS => (IJSInProcessRuntime)runtime;
    public string? Read(string key)
    {
        try { return JS.Invoke<string?>("soulOathStorage.read", key); }
        catch (JSException ex) { throw new IOException("本機存檔無法讀取，請確認瀏覽器允許儲存。", ex); }
    }
    public void Write(string key, string value, string? expected)
    {
        try { JS.InvokeVoid("soulOathStorage.write", key, value, expected); }
        catch (JSException ex) { throw new IOException("本機存檔未寫入：" + ex.Message, ex); }
    }
}
