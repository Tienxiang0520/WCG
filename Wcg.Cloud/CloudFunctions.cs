using System.Text.Json;
using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace WcgCloud;

public sealed class CloudFunctions(CloudIdentity identity, CloudBlobStore store, CloudSaveValidator validator)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Function("Health")]
    public IActionResult Health([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request) 
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return new OkObjectResult(new { service = "WCG Cloud Save", version = 1, data = CloudCatalog.Hashes });
    }

    [Function("PlayerSave")]
    public async Task<IActionResult> Save([HttpTrigger(AuthorizationLevel.Anonymous, "get", "put", Route = "save")] HttpRequest request, CancellationToken token)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        var owner = await identity.Owner(request.Headers.Authorization.FirstOrDefault(), token);
        if (owner is null) return new UnauthorizedObjectResult(new { code = "unauthorized", message = "請重新登入 Microsoft 帳號。" });
        try
        {
            if (HttpMethods.IsGet(request.Method)) return new OkObjectResult(await store.Read(owner, token));
            var etag = request.Headers.IfMatch.FirstOrDefault(); var lease = request.Headers["X-WCG-Lease"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(etag) || etag == "*" || !Guid.TryParse(lease, out _)) return new BadRequestObjectResult(new { code = "precondition_required", message = "請先取得雲端存檔版本與裝置控制權。" });
            var save = await Body<CloudSnapshot>(request, token);
            validator.Validate(save);
            return new OkObjectResult(new { etag = await store.Write(owner, save, etag, lease!, token), updatedAt = DateTimeOffset.UtcNow });
        }
        catch (RequestFailedException e) when (e.Status is 409 or 412) { return new ConflictObjectResult(new { code = "conflict", message = "存檔版本或裝置控制權已改變，原資料保留。" }); }
        catch (Exception e) when (e is InvalidDataException or JsonException or ArgumentException) { return new BadRequestObjectResult(new { code = "invalid_save", message = "存檔驗證未通過，原資料保留。" }); }
    }
    [Function("PlayerSaveLease")]
    public async Task<IActionResult> Lease([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "save/lease")] HttpRequest request, CancellationToken token)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        var owner = await identity.Owner(request.Headers.Authorization.FirstOrDefault(), token);
        if (owner is null) return new UnauthorizedObjectResult(new { code = "unauthorized" });
        try { return new OkObjectResult(await store.Lease(owner, await Body<LeaseRequest>(request, token), token)); }
        catch (RequestFailedException e) when (e.Status is 409 or 412) { return new ConflictObjectResult(new { code = "other_device", message = "另一個裝置正在使用這份存檔。" }); }
        catch (Exception e) when (e is InvalidDataException or JsonException) { return new BadRequestObjectResult(new { code = "invalid_request" }); }
    }
    private static async Task<T> Body<T>(HttpRequest request, CancellationToken token)
    {
        if (!request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) ?? true) throw new InvalidDataException("需要 JSON。");
        if (request.ContentLength > CloudSaveValidator.MaxBytes) throw new InvalidDataException("存檔過大。");
        using var buffer = new MemoryStream(); var chunk = new byte[8192]; int count;
        while ((count = await request.Body.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + count > CloudSaveValidator.MaxBytes) throw new InvalidDataException("存檔過大。");
            buffer.Write(chunk, 0, count);
        }
        return JsonSerializer.Deserialize<T>(buffer.ToArray(), Json) ?? throw new InvalidDataException("存檔為空。");
    }
}
