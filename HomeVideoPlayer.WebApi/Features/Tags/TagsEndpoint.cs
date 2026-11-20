using ErrorOr;

namespace HomeVideoPlayer.WebApi.Features.Tags;

public static class TagsEndpoint
{
    public static IEndpointRouteBuilder MapTagsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/tags/{tagId:int}",
                async (int tagId, GetTagsHandler handler, CancellationToken cancellationToken) =>
                {
                    var tag = await handler.HandleByIdAsync(tagId, cancellationToken);
                    return tag is null ? Results.NotFound() : Results.Ok(tag);
                }
            )
            .WithName("GetTagById")
            .WithSummary("Get a tag by ID")
            .WithDescription("Returns a tag from the shared catalog.")
            .Produces<TagResponse>()
            .Produces(StatusCodes.Status404NotFound);

        endpoints
            .MapGet(
                "/api/tags",
                async (GetTagsHandler handler, CancellationToken cancellationToken) =>
                    Results.Ok(await handler.HandleAsync(cancellationToken))
            )
            .WithName("GetTags")
            .WithSummary("List tags")
            .WithDescription("Returns all tags available for assignment to videos.")
            .Produces<IReadOnlyList<TagResponse>>();

        endpoints
            .MapPost(
                "/api/tags",
                async (
                    CreateTagRequest request,
                    CreateTagHandler handler,
                    CancellationToken cancellationToken
                ) =>
                {
                    var result = await handler.HandleAsync(request, cancellationToken);
                    if (result.IsError)
                    {
                        var error = result.FirstError;
                        var response = new { error.Code, error.Description };
                        return error.Type == ErrorType.Conflict
                            ? Results.Conflict(response)
                            : Results.BadRequest(response);
                    }

                    return Results.Created($"/api/tags/{result.Value.TagId}", result.Value);
                }
            )
            .WithName("CreateTag")
            .WithSummary("Create a tag")
            .WithDescription("Adds a new tag to the shared catalog.")
            .Produces<TagResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status409Conflict);

        endpoints
            .MapDelete(
                "/api/tags/{tagId:int}",
                async (
                    int tagId,
                    DeleteTagHandler handler,
                    CancellationToken cancellationToken
                ) =>
                    await handler.HandleAsync(tagId, cancellationToken)
                        ? Results.NoContent()
                        : Results.NotFound()
            )
            .WithName("DeleteTag")
            .WithSummary("Delete a tag")
            .WithDescription("Removes a tag from the catalog and all videos assigned to it.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
