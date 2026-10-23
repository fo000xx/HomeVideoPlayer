using System.Net;
using System.Net.Http.Headers;
using HomeVideoPlayer.WebApi.Data;
using HomeVideoPlayer.WebApi.Data.Entities;
using HomeVideoPlayer.WebApi.Features.VideoIngestion;
using HomeVideoPlayer.WebApi.Features.Videos.GetVideoStream;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HomeVideoPlayer.WebApi.Tests;

public sealed class GetVideoStreamIntegrationTests
{
    [Fact]
    public async Task GetVideoStream_ServesRangesAndRejectsMissingOrOutOfRootFiles()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var storageDirectory = Path.Combine(temporaryDirectory, "media");
        Directory.CreateDirectory(storageDirectory);
        await File.WriteAllTextAsync(Path.Combine(temporaryDirectory, "outside.mp4"), "outside");
        await File.WriteAllTextAsync(Path.Combine(storageDirectory, "clip.mp4"), "0123456789");

        try
        {
            await using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            builder.Services.AddSingleton<IOptions<VideoIngestionOptions>>(
                Options.Create(
                    new VideoIngestionOptions
                    {
                        InputDirectory = temporaryDirectory,
                        LongTermStorage = storageDirectory,
                        BackupStorage = temporaryDirectory,
                        QuarantineDirectory = temporaryDirectory,
                        SupportedFileTypes = [".mp4"],
                    }
                )
            );
            builder.Services.AddScoped<GetVideoStreamHandler>();

            await using var app = builder.Build();
            app.MapGetVideoStreamEndpoint();
            await app.StartAsync();

            await using (var scope = app.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await dbContext.Database.EnsureCreatedAsync();
                dbContext.Videos.AddRange(
                    CreateVideo("clip.mp4"),
                    CreateVideo("../outside.mp4"),
                    CreateVideo("missing.mp4")
                );
                await dbContext.SaveChangesAsync();
            }

            using var client = app.GetTestClient();
            using var rangeRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "/api/videos/1/stream"
            );
            rangeRequest.Headers.Range = new RangeHeaderValue(2, 5);

            using var rangeResponse = await client.SendAsync(rangeRequest);
            Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
            Assert.Equal("video/mp4", rangeResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal("bytes 2-5/10", rangeResponse.Content.Headers.ContentRange?.ToString());
            Assert.Equal("2345", await rangeResponse.Content.ReadAsStringAsync());

            using var escapedPathResponse = await client.GetAsync("/api/videos/2/stream");
            Assert.Equal(HttpStatusCode.NotFound, escapedPathResponse.StatusCode);

            using var missingFileResponse = await client.GetAsync("/api/videos/3/stream");
            Assert.Equal(HttpStatusCode.NotFound, missingFileResponse.StatusCode);

            using var missingVideoResponse = await client.GetAsync("/api/videos/999/stream");
            Assert.Equal(HttpStatusCode.NotFound, missingVideoResponse.StatusCode);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static Video CreateVideo(string storagePath) =>
        new()
        {
            VideoName = Path.GetFileName(storagePath),
            VideoDate = new DateTime(2026, 1, 1),
            OriginalFileHash = $"original-{storagePath}",
            NewFileHash = $"new-{storagePath}",
            StoragePath = storagePath,
        };
}
