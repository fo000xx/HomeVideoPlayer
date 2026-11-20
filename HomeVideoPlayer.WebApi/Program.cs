using HomeVideoPlayer.WebApi.Data;
using HomeVideoPlayer.WebApi.Features.Tags;
using HomeVideoPlayer.WebApi.Features.VideoIngestion;
using HomeVideoPlayer.WebApi.Features.Videos.GetVideos;
using HomeVideoPlayer.WebApi.Features.Videos.GetVideoStream;
using HomeVideoPlayer.WebApi.Features.Videos.UpdateVideoTags;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

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
builder.Services.AddScoped<GetTagsHandler>();
builder.Services.AddScoped<CreateTagHandler>();
builder.Services.AddScoped<DeleteTagHandler>();
builder.Services.AddScoped<UpdateVideoTagsHandler>();
builder.Services.AddScoped<GetVideoStreamHandler>();

var app = builder.Build();
app.MapGetVideosEndpoint();
app.MapTagsEndpoints();
app.MapUpdateVideoTagsEndpoint();
app.MapGetVideoStreamEndpoint();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
app.Run();

public partial class Program { }
