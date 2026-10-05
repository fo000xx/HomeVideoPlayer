using HomeVideoPlayer.WebApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HomeVideoPlayer.WebApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Video> Videos => Set<Video>();
}
