namespace HomeVideoPlayer.WebApi.Features.VideoIngestion;

public class VideoIngestionOptions
{
    public const string SectionName = "VideoIngestionOptions";

    public required string InputDirectory { get; set; }
    public int PollIntervalSeconds { get; set; } = 60;
    public required string LongTermStorage { get; set; }
    public required string BackupStorage { get; set; }
}
