using ErrorOr;

namespace HomeVideoPlayer.WebApi.Features.Videos.UpdateVideoTags;

public static class UpdateVideoTagsEndpoint
{
    public static IEndpointRouteBuilder MapUpdateVideoTagsEndpoint(
        this IEndpointRouteBuilder endpoints
    )
    {
        endpoints
            .MapPost(
                "/api/videos/{videoId:int}/tags",
                async (
                    int videoId,
                    UpdateVideoTagsRequest request,
                    UpdateVideoTagsHandler handler,
                    CancellationToken cancellationToken
                ) =>
                {
                    var result = await handler.HandleAsync(videoId, request, cancellationToken);
                    if (result.IsError)
                    {
                        var error = result.FirstError;
                        var response = new { error.Code, error.Description };
                        return error.Type switch
                        {
                            ErrorType.NotFound => Results.NotFound(response),
                            _ => Results.BadRequest(response),
                        };
                    }

                    return Results.Ok(result.Value);
                }
            )
            .WithName("ReplaceVideoTags")
            .WithSummary("Replace a video's tags")
            .WithDescription(
                "Replaces the video's complete tag list. Submit every tag ID that should remain assigned."
            )
            .Produces<UpdateVideoTagsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
