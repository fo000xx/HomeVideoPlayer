using ErrorOr;
using HomeVideoPlayer.WebApi.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeVideoPlayer.WebApi.Features.Videos.UpdateVideoTags;

public sealed class UpdateVideoTagsHandler(AppDbContext dbContext)
{
    public async Task<ErrorOr<UpdateVideoTagsResponse>> HandleAsync(
        int videoId,
        UpdateVideoTagsRequest request,
        CancellationToken cancellationToken
    )
    {
        var video = await dbContext
            .Videos.Include(item => item.Tags)
            .SingleOrDefaultAsync(item => item.VideoId == videoId, cancellationToken);
        if (video is null)
        {
            return Error.NotFound(code: "VideoNotFound", description: "The video does not exist.");
        }

        if (request.TagIds is null)
        {
            return Error.Validation(code: "InvalidTagIds", description: "tagIds is required.");
        }

        var tagIds = request.TagIds.Distinct().ToArray();
        var tags = await dbContext
            .Tags.Where(tag => tagIds.Contains(tag.TagId))
            .ToListAsync(cancellationToken);
        if (tags.Count != tagIds.Length)
        {
            return Error.Validation(
                code: "InvalidTagIds",
                description: "All tagIds must refer to existing tags."
            );
        }

        video.Tags = tags;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new UpdateVideoTagsResponse(
            video.VideoId,
            tags.Select(tag => tag.Name).ToArray(),
            tags.Select(tag => tag.TagId).ToArray()
        );
    }
}
