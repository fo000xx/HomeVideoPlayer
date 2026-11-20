namespace HomeVideoPlayer.WebApi.Data.Entities;

public class Tag
{
    public int TagId { get; set; }
    public required string Name { get; set; }
    public List<Video> Videos { get; set; } = [];
}
