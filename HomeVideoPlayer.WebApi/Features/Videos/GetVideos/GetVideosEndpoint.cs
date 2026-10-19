using Microsoft.AspNetCore.Mvc;

namespace HomeVideoPlayer.WebApi.Features.Videos.GetVideos;

public static class GetVideosEndpoint
{
    public static IEndpointRouteBuilder MapGetVideosEndpoint(
        this IEndpointRouteBuilder endpoints
    )
    {
        endpoints.MapGet(
            "/api/videos",
            async (
                [AsParameters] GetVideosRequest request,
                GetVideosHandler handler,
                CancellationToken cancellationToken
            ) =>
            {
                var result = await handler.HandleAsync(request, cancellationToken);
                if (result.IsError)
                {
                    var error = result.FirstError;
                    return Results.BadRequest(new GetVideosError(error.Code, error.Description));
                }

                return Results.Ok(result.Value);
            }
        );

        return endpoints;
    }
}