namespace HomeVideoPlayer.WebApi.Features.Videos.UpdateVideoTags;

public sealed record UpdateVideoTagsResponse(
    int VideoId,
    IReadOnlyList<string> Tags,
    IReadOnlyList<int> TagIds
);
