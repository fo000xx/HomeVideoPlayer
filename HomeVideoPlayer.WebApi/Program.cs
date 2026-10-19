using HomeVideoPlayer.WebApi.Data;
using HomeVideoPlayer.WebApi.Features.VideoIngestion;
using HomeVideoPlayer.WebApi.Features.Videos.GetVideos;
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

var app = builder.Build();
app.MapGetVideosEndpoint();
app.Run();

public partial class Program { }
