namespace HomeVideoPlayer.WebApi.Features.Videos.GetVideos;

public sealed record GetVideosItemResponse(
    int VideoId,
    string VideoName,
    DateTime VideoDate,
    IReadOnlyList<string> Tags
);

public sealed record GetVideosResponse(
    IReadOnlyList<GetVideosItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount
);