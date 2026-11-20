using ErrorOr;
using HomeVideoPlayer.WebApi.Data;
using HomeVideoPlayer.WebApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeVideoPlayer.WebApi.Features.Tags;

public sealed class CreateTagHandler(AppDbContext dbContext)
{
    public async Task<ErrorOr<TagResponse>> HandleAsync(
        CreateTagRequest request,
        CancellationToken cancellationToken
    )
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation(code: "InvalidTagName", description: "Tag name is required.");
        }

        if (name.Length > 100)
        {
            return Error.Validation(
                code: "InvalidTagName",
                description: "Tag names must be 100 characters or fewer."
            );
        }

        if (await dbContext.Tags.AnyAsync(tag => tag.Name == name, cancellationToken))
        {
            return Error.Conflict(
                code: "DuplicateTagName",
                description: "A tag with this name already exists."
            );
        }

        var tag = new Tag { Name = name };
        dbContext.Tags.Add(tag);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new TagResponse(tag.TagId, tag.Name, 0);
    }
}
