using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace WcgCloud;

public sealed class CloudBlobStore(BlobContainerClient container)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private BlobClient Blob(string owner) => container.GetBlobClient($"players/{owner}/save.json");
    public async Task<CloudRead> Read(string owner, CancellationToken token)
    {
        try
        {
            var content = (await Blob(owner).DownloadContentAsync(token)).Value;
            var saved = content.Content.ToObjectFromJson<StoredSave>(Json)!;
            return new(saved.Initialized, content.Details.ETag.ToString(), saved.Initialized ? saved.Save : null, saved.Initialized ? saved.UpdatedAt : null);
        }
        catch (RequestFailedException e) when (e.Status == 404) { return new(false, null, null, null); }
    }
    public async Task<string> Write(string owner, CloudSnapshot save, string etag, string lease, CancellationToken token)
    {
        var result = await Blob(owner).UploadAsync(BinaryData.FromObjectAsJson(new StoredSave(true, DateTimeOffset.UtcNow, save), Json), new BlobUploadOptions
        {
            Conditions = new() { IfMatch = new ETag(etag), LeaseId = lease },
            HttpHeaders = new() { ContentType = "application/json", CacheControl = "no-store" }
        }, token);
        return result.Value.ETag.ToString();
    }
    public async Task<object> Lease(string owner, LeaseRequest request, CancellationToken token)
    {
        if (!Guid.TryParse(request.LeaseId, out var leaseId)) throw new InvalidDataException("裝置識別碼不正確。");
        var blob = Blob(owner);
        var lease = blob.GetBlobLeaseClient(leaseId.ToString());
        switch (request.Action)
        {
            case "acquire": case "takeover":
                try
                {
                    await blob.UploadAsync(BinaryData.FromObjectAsJson(new StoredSave(false, DateTimeOffset.UtcNow, new(new(), new())), Json), new BlobUploadOptions { Conditions = new() { IfNoneMatch = ETag.All }, HttpHeaders = new() { ContentType = "application/json", CacheControl = "no-store" } }, token);
                }
                catch (RequestFailedException e) when (e.Status is 409 or 412) { }
                if (request.Action == "takeover")
                {
                    try { await blob.GetBlobLeaseClient().BreakAsync(TimeSpan.Zero, cancellationToken: token); }
                    catch (RequestFailedException e) when (e.ErrorCode == "LeaseNotPresentWithLeaseOperation") { }
                }
                try { await lease.AcquireAsync(TimeSpan.FromSeconds(60), cancellationToken: token); }
                catch (RequestFailedException e) when (e.Status == 409)
                {
                    // The same tab can reacquire its existing session after a transient response loss.
                    await lease.RenewAsync(cancellationToken: token);
                }
                break;
            case "renew": await lease.RenewAsync(cancellationToken: token); break;
            case "release": await lease.ReleaseAsync(cancellationToken: token); break;
            default: throw new InvalidDataException("不支援的裝置操作。");
        }
        return new { leaseId = leaseId.ToString(), expiresIn = 60 };
    }
}
