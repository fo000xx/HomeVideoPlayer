using Microsoft.AspNetCore.StaticFiles;

namespace HomeVideoPlayer.WebApi.Features.Videos.GetVideoStream;

public static class GetVideoStreamEndpoint
{
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public static IEndpointRouteBuilder MapGetVideoStreamEndpoint(
        this IEndpointRouteBuilder endpoints
    )
    {
        endpoints
            .MapGet(
                "/api/videos/{videoId:int}/stream",
                async (
                    int videoId,
                    GetVideoStreamHandler handler,
                    CancellationToken cancellationToken
                ) =>
                {
                    var filePath = await handler.HandleAsync(videoId, cancellationToken);
                    if (filePath is null)
                    {
                        return Results.NotFound();
                    }

                    var contentType = ContentTypeProvider.TryGetContentType(filePath, out var type)
                        ? type
                        : "application/octet-stream";

                    return Results.File(filePath, contentType, enableRangeProcessing: true);
                }
            )
            .WithName("GetVideoStream")
            .WithSummary("Stream a video")
            .WithDescription("Streams a video file with byte-range support.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
