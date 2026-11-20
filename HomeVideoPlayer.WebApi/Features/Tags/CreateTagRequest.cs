namespace HomeVideoPlayer.WebApi.Features.Tags;

public sealed record CreateTagRequest
{
    public required string Name { get; init; }
}
