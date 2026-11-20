namespace HomeVideoPlayer.WebApi.Features.Videos.UpdateVideoTags;

public sealed record UpdateVideoTagsRequest
{
    public required int[] TagIds { get; init; }
}
