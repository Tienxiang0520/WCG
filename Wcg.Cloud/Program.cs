using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WcgCloud;

var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();
builder.Services.AddSingleton(new CloudIdentity(Environment.GetEnvironmentVariable("WCG_API_CLIENT_ID") ?? throw new InvalidOperationException("Missing API audience.")));
builder.Services.AddSingleton(new BlobContainerClient(new Uri($"https://{Environment.GetEnvironmentVariable("WCG_STORAGE_ACCOUNT")}.blob.core.windows.net/player-saves"), new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)));
builder.Services.AddSingleton<CloudSaveValidator>();
builder.Services.AddSingleton<CloudBlobStore>();
builder.Build().Run();
