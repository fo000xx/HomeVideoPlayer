namespace HomeVideoPlayer.WebApi.Data.Entities;

public class Video
{
    public int VideoId { get; set; }
    public required string VideoName { get; set; }
    public required DateTime VideoDate { get; set; }
    public required string FileHash { get; set; }

    // todo-bf: this should be a json array instead, in the db?
    public List<string>? Tags { get; set; }
}
