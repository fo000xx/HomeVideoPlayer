using HomeVideoPlayer.WebApi.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeVideoPlayer.WebApi.Features.Tags;

public sealed class GetTagsHandler(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<TagResponse>> HandleAsync(
        CancellationToken cancellationToken
    ) =>
        await dbContext
            .Tags.AsNoTracking()
            .OrderBy(tag => tag.Name)
            .Select(tag => new TagResponse(tag.TagId, tag.Name, tag.Videos.Count))
            .ToListAsync(cancellationToken);

    public async Task<TagResponse?> HandleByIdAsync(
        int tagId,
        CancellationToken cancellationToken
    ) =>
        await dbContext
            .Tags.AsNoTracking()
            .Where(tag => tag.TagId == tagId)
            .Select(tag => new TagResponse(tag.TagId, tag.Name, tag.Videos.Count))
            .SingleOrDefaultAsync(cancellationToken);
}
