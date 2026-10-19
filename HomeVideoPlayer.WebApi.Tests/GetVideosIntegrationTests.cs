using System.Net;
using System.Text.Json;
using HomeVideoPlayer.WebApi.Data;
using HomeVideoPlayer.WebApi.Data.Entities;
using HomeVideoPlayer.WebApi.Features.Videos.GetVideos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeVideoPlayer.WebApi.Tests;

public sealed class GetVideosIntegrationTests
{
    [Fact]
    public async Task GetVideos_ReturnsSortedPageWithoutInternalPathsOrHashes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddScoped<GetVideosHandler>();

        await using var app = builder.Build();
        app.MapGetVideosEndpoint();
        await app.StartAsync();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.Videos.AddRange(
                CreateVideo("Charlie", new DateTime(2026, 3, 1)),
                CreateVideo("Alpha", new DateTime(2026, 1, 1), ["home"]),
                CreateVideo("Bravo", new DateTime(2026, 2, 1))
            );
            await dbContext.SaveChangesAsync();
        }

        using var client = app.GetTestClient();
        using var response = await client.GetAsync(
            "/api/videos?page=1&pageSize=2&sortBy=name&sortOrder=asc"
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(2, root.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, root.GetProperty("totalCount").GetInt32());

        var items = root.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(
            new[] { "Alpha", "Bravo" },
            items.Select(item => item.GetProperty("videoName").GetString())
        );
        Assert.Equal("home", items[0].GetProperty("tags")[0].GetString());
        Assert.Empty(items[1].GetProperty("tags").EnumerateArray());

        var responseText = root.GetRawText();
        Assert.DoesNotContain("storagePath", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("backupPath", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("originalFileHash", responseText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("newFileHash", responseText, StringComparison.OrdinalIgnoreCase);

        using var secondPageResponse = await client.GetAsync(
            "/api/videos?page=2&pageSize=2&sortBy=name&sortOrder=asc"
        );
        using var secondPageJson = JsonDocument.Parse(
            await secondPageResponse.Content.ReadAsStringAsync()
        );
        Assert.Equal(
            "Charlie",
            secondPageJson.RootElement.GetProperty("items")[0].GetProperty("videoName").GetString()
        );
    }

    [Fact]
    public async Task GetVideos_RejectsUnsupportedSortField()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite("Data Source=:memory:")
        );
        builder.Services.AddScoped<GetVideosHandler>();

        await using var app = builder.Build();
        app.MapGetVideosEndpoint();
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/videos?sortBy=storagePath");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Video CreateVideo(string name, DateTime date, List<string>? tags = null) =>
        new()
        {
            VideoName = name,
            VideoDate = date,
            OriginalFileHash = $"original-{name}",
            NewFileHash = $"new-{name}",
            StoragePath = $"storage/{name}.mp4",
            BackupPath = $"backup/{name}.mts",
            Tags = tags,
        };
}
