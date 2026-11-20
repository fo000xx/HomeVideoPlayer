using System.Globalization;
using ErrorOr;
using HomeVideoPlayer.WebApi.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeVideoPlayer.WebApi.Features.Videos.GetVideos;

public sealed class GetVideosHandler(AppDbContext dbContext)
{
    public async Task<ErrorOr<GetVideosResponse>> HandleAsync(
        GetVideosRequest request,
        string? rawTagIds,
        CancellationToken cancellationToken
    )
    {
        if (
            !string.Equals(request.SortBy, "name", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.SortBy, "date", StringComparison.OrdinalIgnoreCase)
        )
        {
            return Error.Validation(
                code: "InvalidSortBy",
                description: "sortBy must be 'name' or 'date'."
            );
        }

        if (
            !string.Equals(request.SortOrder, "asc", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.SortOrder, "desc", StringComparison.OrdinalIgnoreCase)
        )
        {
            return Error.Validation(
                code: "InvalidSortOrder",
                description: "sortOrder must be 'asc' or 'desc'."
            );
        }

        var page = Math.Max(request.Page, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var videos = dbContext.Videos.AsNoTracking();
        var tagIds = Array.Empty<int>();
        if (!string.IsNullOrWhiteSpace(rawTagIds))
        {
            var parsedTagIds = new List<int>();
            foreach (var value in rawTagIds.Split(','))
            {
                if (
                    !int.TryParse(
                        value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var tagId
                    )
                )
                {
                    return Error.Validation(
                        code: "InvalidTagIds",
                        description: "tagIds must be a comma-separated list of tag IDs."
                    );
                }

                parsedTagIds.Add(tagId);
            }

            tagIds = parsedTagIds.Distinct().ToArray();
        }

        if (tagIds.Length > 0)
        {
            var knownTagCount = await dbContext.Tags.CountAsync(
                tag => tagIds.Contains(tag.TagId),
                cancellationToken
            );
            if (knownTagCount != tagIds.Length)
            {
                return Error.Validation(
                    code: "InvalidTagIds",
                    description: "All tagIds must refer to existing tags."
                );
            }

            foreach (var tagId in tagIds)
            {
                var matchingTagId = tagId;
                videos = videos.Where(video => video.Tags.Any(tag => tag.TagId == matchingTagId));
            }
        }

        var totalCount = await videos.CountAsync(cancellationToken);
        var descending = string.Equals(
            request.SortOrder,
            "desc",
            StringComparison.OrdinalIgnoreCase
        );

        var orderedVideos = string.Equals(
            request.SortBy,
            "name",
            StringComparison.OrdinalIgnoreCase
        )
            ? descending
                ? videos.OrderByDescending(video => video.VideoName)
                : videos.OrderBy(video => video.VideoName)
            : descending
                ? videos.OrderByDescending(video => video.VideoDate)
                : videos.OrderBy(video => video.VideoDate);

        var offset = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var items = await orderedVideos
            .ThenBy(video => video.VideoId)
            .Skip(offset)
            .Take(pageSize)
            .Select(video => new GetVideosItemResponse(
                video.VideoId,
                video.VideoName,
                video.VideoDate,
                video.Tags.Select(tag => tag.Name).ToList()
            ))
            .ToListAsync(cancellationToken);

        return new GetVideosResponse(items, page, pageSize, totalCount);
    }
}
