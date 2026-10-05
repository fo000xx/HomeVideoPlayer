namespace HomeVideoPlayer.WebApi.Data.Entities;

public class Video
{
    public int VideoId { get; set; }
    public required string VideoName { get; set; }
    public string? StoragePath { get; set; }
    public string? BackupPath { get; set; }
    public required DateTime VideoDate { get; set; }
    public required string OriginalFileHash { get; set; }
    public required string NewFileHash { get; set; }

    // todo-bf: this should be a json array instead, in the db?
    public List<string>? Tags { get; set; }
}
