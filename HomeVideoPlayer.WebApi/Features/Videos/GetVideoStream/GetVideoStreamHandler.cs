using HomeVideoPlayer.WebApi.Data;
using HomeVideoPlayer.WebApi.Features.VideoIngestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeVideoPlayer.WebApi.Features.Videos.GetVideoStream;

public sealed class GetVideoStreamHandler(
    AppDbContext dbContext,
    IOptions<VideoIngestionOptions> ingestionOptions,
    ILogger<GetVideoStreamHandler> logger
)
{
    public async Task<string?> HandleAsync(int videoId, CancellationToken cancellationToken)
    {
        var video = await dbContext
            .Videos.AsNoTracking()
            .Where(item => item.VideoId == videoId)
            .Select(item => new { item.StoragePath })
            .SingleOrDefaultAsync(cancellationToken);

        if (video is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(video.StoragePath) || Path.IsPathRooted(video.StoragePath))
        {
            logger.LogWarning("Video {VideoId} has an invalid storage path.", videoId);
            return null;
        }

        var storageRoot = Path.GetFullPath(ingestionOptions.Value.LongTermStorage);
        var filePath = Path.GetFullPath(Path.Combine(storageRoot, video.StoragePath));
        var relativePath = Path.GetRelativePath(storageRoot, filePath);
        if (
            Path.IsPathRooted(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal
            )
        )
        {
            logger.LogWarning("Video {VideoId} resolves outside the media storage directory.", videoId);
            return null;
        }

        var currentPath = storageRoot;
        foreach (
            var pathSegment in relativePath.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            currentPath = Path.Combine(currentPath, pathSegment);
            if (
                (File.Exists(currentPath) || Directory.Exists(currentPath))
                && (File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0
            )
            {
                logger.LogWarning("Video {VideoId} resolves through a symbolic link.", videoId);
                return null;
            }
        }

        if (!File.Exists(filePath))
        {
            logger.LogWarning("Video {VideoId} was not found at its stored location.", videoId);
            return null;
        }

        return filePath;
    }
}
