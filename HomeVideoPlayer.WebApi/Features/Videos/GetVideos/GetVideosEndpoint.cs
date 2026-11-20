using System.Globalization;

namespace HomeVideoPlayer.WebApi.Features.Videos.GetVideos;

public static class GetVideosEndpoint
{
    public static IEndpointRouteBuilder MapGetVideosEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGet(
                "/api/videos",
                async (
                    HttpRequest httpRequest,
                    GetVideosHandler handler,
                    CancellationToken cancellationToken
                ) =>
                {
                    var request = new GetVideosRequest();
                    if (httpRequest.Query.TryGetValue("page", out var pageValue))
                    {
                        if (
                            !int.TryParse(
                                pageValue.ToString(),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out var page
                            )
                        )
                        {
                            return Results.BadRequest(
                                new GetVideosError("InvalidPage", "page must be an integer.")
                            );
                        }

                        request.Page = page;
                    }

                    if (httpRequest.Query.TryGetValue("pageSize", out var pageSizeValue))
                    {
                        if (
                            !int.TryParse(
                                pageSizeValue.ToString(),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out var pageSize
                            )
                        )
                        {
                            return Results.BadRequest(
                                new GetVideosError(
                                    "InvalidPageSize",
                                    "pageSize must be an integer."
                                )
                            );
                        }

                        request.PageSize = pageSize;
                    }

                    if (httpRequest.Query.TryGetValue("sortBy", out var sortBy))
                    {
                        request.SortBy = sortBy.ToString();
                    }

                    if (httpRequest.Query.TryGetValue("sortOrder", out var sortOrder))
                    {
                        request.SortOrder = sortOrder.ToString();
                    }

                    var result = await handler.HandleAsync(
                        request,
                        httpRequest.Query["tagIds"].ToString(),
                        cancellationToken
                    );
                    if (result.IsError)
                    {
                        var error = result.FirstError;
                        return Results.BadRequest(
                            new GetVideosError(error.Code, error.Description)
                        );
                    }

                    return Results.Ok(result.Value);
                }
            )
            .WithName("GetVideos")
            .WithSummary("List videos")
            .WithDescription(
                "Returns a sorted page of videos. Filter by one or more comma-separated tag IDs; videos must match every selected tag."
            )
            .Produces<GetVideosResponse>()
            .Produces(StatusCodes.Status400BadRequest);

        return endpoints;
    }
}
