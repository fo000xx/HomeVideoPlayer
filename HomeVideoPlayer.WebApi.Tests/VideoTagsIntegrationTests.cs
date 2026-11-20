using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeVideoPlayer.WebApi.Data;
using HomeVideoPlayer.WebApi.Data.Entities;
using HomeVideoPlayer.WebApi.Features.Tags;
using HomeVideoPlayer.WebApi.Features.Videos.GetVideos;
using HomeVideoPlayer.WebApi.Features.Videos.UpdateVideoTags;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HomeVideoPlayer.WebApi.Tests;

public sealed class VideoTagsIntegrationTests
{
    [Fact]
    public async Task Tags_CanBeCreatedAssignedAndFiltered()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var app = CreateApp(connection);
        await app.StartAsync();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.Videos.Add(CreateVideo("Family picnic", new DateTime(2026, 1, 1)));
            await dbContext.SaveChangesAsync();
        }

        using var client = app.GetTestClient();
        using var invalidNameResponse = await client.PostAsJsonAsync(
            "/api/tags",
            new { name = "  " }
        );
        Assert.Equal(HttpStatusCode.BadRequest, invalidNameResponse.StatusCode);

        using var createResponse = await client.PostAsJsonAsync(
            "/api/tags",
            new { name = " family " }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var createdTag = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());
        var tagId = createdTag.RootElement.GetProperty("tagId").GetInt32();
        Assert.Equal("family", createdTag.RootElement.GetProperty("name").GetString());

        using var secondTagResponse = await client.PostAsJsonAsync(
            "/api/tags",
            new { name = "travel" }
        );
        Assert.Equal(HttpStatusCode.Created, secondTagResponse.StatusCode);
        using var secondTag = JsonDocument.Parse(await secondTagResponse.Content.ReadAsStringAsync());
        var secondTagId = secondTag.RootElement.GetProperty("tagId").GetInt32();

        using var duplicateResponse = await client.PostAsJsonAsync(
            "/api/tags",
            new { name = "FAMILY" }
        );
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        using var assignmentResponse = await client.PostAsJsonAsync(
            "/api/videos/1/tags",
            new { tagIds = new[] { tagId } }
        );
        Assert.Equal(HttpStatusCode.OK, assignmentResponse.StatusCode);

        using var replacementResponse = await client.PostAsJsonAsync(
            "/api/videos/1/tags",
            new { tagIds = new[] { secondTagId } }
        );
        Assert.Equal(HttpStatusCode.OK, replacementResponse.StatusCode);
        using var replacement = JsonDocument.Parse(
            await replacementResponse.Content.ReadAsStringAsync()
        );
        Assert.Equal(
            new[] { "travel" },
            replacement.RootElement.GetProperty("tags").EnumerateArray()
                .Select(tag => tag.GetString())
        );

        using var invalidAssignmentResponse = await client.PostAsJsonAsync(
            "/api/videos/1/tags",
            new { tagIds = new[] { int.MaxValue } }
        );
        Assert.Equal(HttpStatusCode.BadRequest, invalidAssignmentResponse.StatusCode);

        using var filteredResponse = await client.GetAsync($"/api/videos?tagIds={secondTagId}");
        var filteredResponseBody = await filteredResponse.Content.ReadAsStringAsync();
        Assert.True(
            filteredResponse.StatusCode == HttpStatusCode.OK,
            $"Expected OK, got {(int)filteredResponse.StatusCode} {filteredResponse.ReasonPhrase}; response headers={filteredResponse.Headers}; {filteredResponse.Content.Headers}: {filteredResponseBody}"
        );
        using var filteredJson = JsonDocument.Parse(filteredResponseBody);
        var result = filteredJson.RootElement;
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal("travel", result.GetProperty("items")[0].GetProperty("tags")[0].GetString());
        Assert.False(result.GetProperty("items")[0].TryGetProperty("tagIds", out _));

        using var removedTagFilterResponse = await client.GetAsync($"/api/videos?tagIds={tagId}");
        using var removedTagFilter = JsonDocument.Parse(
            await removedTagFilterResponse.Content.ReadAsStringAsync()
        );
        Assert.Equal(0, removedTagFilter.RootElement.GetProperty("totalCount").GetInt32());

        using var tagsResponse = await client.GetAsync("/api/tags");
        using var tagsJson = JsonDocument.Parse(await tagsResponse.Content.ReadAsStringAsync());
        Assert.Equal("family", tagsJson.RootElement[0].GetProperty("name").GetString());
        Assert.Equal(0, tagsJson.RootElement[0].GetProperty("videoCount").GetInt32());
        Assert.Equal(1, tagsJson.RootElement[1].GetProperty("videoCount").GetInt32());

        using var tagResponse = await client.GetAsync($"/api/tags/{tagId}");
        using var tagJson = JsonDocument.Parse(await tagResponse.Content.ReadAsStringAsync());
        Assert.Equal("family", tagJson.RootElement.GetProperty("name").GetString());
        Assert.Equal(0, tagJson.RootElement.GetProperty("videoCount").GetInt32());

        using var deleteResponse = await client.DeleteAsync($"/api/tags/{secondTagId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var tagsAfterDeleteResponse = await client.GetAsync("/api/tags");
        using var tagsAfterDelete = JsonDocument.Parse(
            await tagsAfterDeleteResponse.Content.ReadAsStringAsync()
        );
        Assert.DoesNotContain(
            tagsAfterDelete.RootElement.EnumerateArray(),
            tag => tag.GetProperty("tagId").GetInt32() == secondTagId
        );

        using var videosAfterDeleteResponse = await client.GetAsync("/api/videos");
        using var videosAfterDelete = JsonDocument.Parse(
            await videosAfterDeleteResponse.Content.ReadAsStringAsync()
        );
        Assert.Empty(videosAfterDelete.RootElement.GetProperty("items")[0].GetProperty("tags").EnumerateArray());

        using var deleteMissingResponse = await client.DeleteAsync($"/api/tags/{secondTagId}");
        Assert.Equal(HttpStatusCode.NotFound, deleteMissingResponse.StatusCode);
    }

    [Fact]
    public async Task GetVideos_WithMultipleTagsRequiresAllAndPaginatesFilteredResults()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var app = CreateApp(connection);
        await app.StartAsync();

        int homeTagId;
        int travelTagId;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.EnsureCreatedAsync();

            var home = new Tag { Name = "home" };
            var travel = new Tag { Name = "travel" };
            dbContext.Tags.AddRange(home, travel);
            dbContext.Videos.AddRange(
                CreateVideo("Alpha", new DateTime(2026, 1, 1), [home, travel]),
                CreateVideo("Bravo", new DateTime(2026, 2, 1), [home, travel]),
                CreateVideo("Charlie", new DateTime(2026, 3, 1), [home])
            );
            await dbContext.SaveChangesAsync();
            homeTagId = home.TagId;
            travelTagId = travel.TagId;
        }

        using var client = app.GetTestClient();
        var query =
            $"/api/videos?tagIds={homeTagId},{travelTagId}&sortBy=name&sortOrder=asc&pageSize=1";
        using var firstPageResponse = await client.GetAsync(query);
        var firstPageBody = await firstPageResponse.Content.ReadAsStringAsync();
        Assert.True(
            firstPageResponse.StatusCode == HttpStatusCode.OK,
            $"Expected OK, got {(int)firstPageResponse.StatusCode} {firstPageResponse.ReasonPhrase}; {firstPageResponse.Content.Headers}: {firstPageBody}"
        );
        using var firstPageJson = JsonDocument.Parse(firstPageBody);
        Assert.Equal(2, firstPageJson.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(
            "Alpha",
            firstPageJson.RootElement.GetProperty("items")[0].GetProperty("videoName").GetString()
        );

        using var secondPageResponse = await client.GetAsync($"{query}&page=2");
        using var secondPageJson = JsonDocument.Parse(
            await secondPageResponse.Content.ReadAsStringAsync()
        );
        Assert.Equal(
            "Bravo",
            secondPageJson.RootElement.GetProperty("items")[0].GetProperty("videoName").GetString()
        );
    }

    [Fact]
    public async Task GetVideos_RejectsInvalidTagFilters()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var app = CreateApp(connection);
        await app.StartAsync();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await dbContext.Database.EnsureCreatedAsync();
        }

        using var client = app.GetTestClient();
        using var unknownTagResponse = await client.GetAsync("/api/videos?tagIds=123");
        Assert.Equal(HttpStatusCode.BadRequest, unknownTagResponse.StatusCode);

        using var malformedTagResponse = await client.GetAsync("/api/videos?tagIds=not-a-number");
        Assert.Equal(HttpStatusCode.BadRequest, malformedTagResponse.StatusCode);
    }

    private static WebApplication CreateApp(SqliteConnection connection)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddScoped<GetVideosHandler>();
        builder.Services.AddScoped<GetTagsHandler>();
        builder.Services.AddScoped<CreateTagHandler>();
        builder.Services.AddScoped<DeleteTagHandler>();
        builder.Services.AddScoped<UpdateVideoTagsHandler>();

        var app = builder.Build();
        app.MapGetVideosEndpoint();
        app.MapTagsEndpoints();
        app.MapUpdateVideoTagsEndpoint();
        return app;
    }

    private static Video CreateVideo(string name, DateTime date, List<Tag>? tags = null) =>
        new()
        {
            VideoName = name,
            VideoDate = date,
            OriginalFileHash = $"original-{name}",
            NewFileHash = $"new-{name}",
            Tags = tags ?? [],
        };
}
