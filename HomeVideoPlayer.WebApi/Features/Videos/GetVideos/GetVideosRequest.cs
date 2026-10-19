namespace HomeVideoPlayer.WebApi.Features.Videos.GetVideos;

public sealed class GetVideosRequest
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "date";
    public string SortOrder { get; set; } = "desc";
}