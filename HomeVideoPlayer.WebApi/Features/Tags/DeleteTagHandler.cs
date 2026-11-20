using HomeVideoPlayer.WebApi.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeVideoPlayer.WebApi.Features.Tags;

public sealed class DeleteTagHandler(AppDbContext dbContext)
{
    public async Task<bool> HandleAsync(int tagId, CancellationToken cancellationToken)
    {
        var tag = await dbContext.Tags.SingleOrDefaultAsync(
            tag => tag.TagId == tagId,
            cancellationToken
        );
        if (tag is null) return false;

        dbContext.Tags.Remove(tag);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
