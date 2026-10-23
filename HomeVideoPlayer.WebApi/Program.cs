using HomeVideoPlayer.WebApi.Data;
using HomeVideoPlayer.WebApi.Features.VideoIngestion;
using HomeVideoPlayer.WebApi.Features.Videos.GetVideos;
using HomeVideoPlayer.WebApi.Features.Videos.GetVideoStream;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder
    .Services.AddOptions<VideoIngestionOptions>()
    .Bind(builder.Configuration.GetSection(VideoIngestionOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.InputDirectory),
        "VideoIngestionOptions:InputDirectory must be configured"
    )
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.LongTermStorage),
        "VideoIngestionOptions:LongTermStorage must be configured"
    )
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.BackupStorage),
        "VideoIngestionOptions:BackupStorage must be configured"
    )
    .ValidateOnStart();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddHostedService<VideoIngestionBackgroundService>();
builder.Services.AddScoped<GetVideosHandler>();
builder.Services.AddScoped<GetVideoStreamHandler>();

var app = builder.Build();
app.MapGetVideosEndpoint();
app.MapGetVideoStreamEndpoint();
app.Run();

public partial class Program { }
