using LifeOs.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOs.Api.Data;

public sealed class LifeOsDbContext(DbContextOptions<LifeOsDbContext> options) : DbContext(options)
{
    public DbSet<Capture> Captures => Set<Capture>();
    public DbSet<LifeOs.Api.Services.AiSettingsRow> AiSettings => Set<LifeOs.Api.Services.AiSettingsRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var capture = modelBuilder.Entity<Capture>();
        capture.HasKey(x => x.Id);
        capture.Property(x => x.Source).HasConversion<string>();
        capture.Property(x => x.Type).HasConversion<string>();
        capture.Property(x => x.ProcessingState).HasConversion<string>();
        capture.Property(x => x.Tags).HasColumnType("text[]");
        capture.Property(x => x.MetadataJson).HasColumnType("jsonb");
        capture.HasIndex(x => x.CapturedAt);
        capture.HasIndex(x => x.CategoryId);
        var settings = modelBuilder.Entity<LifeOs.Api.Services.AiSettingsRow>();
        settings.HasKey(x => x.Id);
        settings.Property(x => x.Revision).IsConcurrencyToken();
        settings.Property(x => x.DraftJson).HasColumnType("jsonb");
        settings.Property(x => x.ActiveJson).HasColumnType("jsonb");
    }
}
