using ErrorOr;
using HomeVideoPlayer.WebApi.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeVideoPlayer.WebApi.Features.Videos.GetVideos;

public sealed class GetVideosHandler(AppDbContext dbContext)
{
    public async Task<ErrorOr<GetVideosResponse>> HandleAsync(
        GetVideosRequest request,
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
                (IReadOnlyList<string>?)video.Tags ?? Array.Empty<string>()
            ))
            .ToListAsync(cancellationToken);

        return new GetVideosResponse(items, page, pageSize, totalCount);
    }
}
