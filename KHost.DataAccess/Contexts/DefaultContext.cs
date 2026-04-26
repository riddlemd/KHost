using KHost.Abstractions.Models;
using Microsoft.EntityFrameworkCore;

namespace KHost.DataAccess.Contexts;

internal class DefaultContext : DbContext
{
    public DbSet<Media> Media { get; set; }
    public DbSet<Venue> Venues { get; set; }
    public DbSet<KHostUser> Users { get; set; }
    public DbSet<Performance> Performances { get; set; }

    public DefaultContext(DbContextOptions<DefaultContext> options) : base(options)
    {

    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Media>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.FilePath)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.Title)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Artist)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Format)
                .IsRequired()
                .HasMaxLength(10);

            entity.Property(e => e.Notes)
                .HasMaxLength(1000);

            entity.HasIndex(e => e.FilePath).IsUnique();
            entity.HasIndex(e => e.Title);
            entity.HasIndex(e => e.Artist);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.DateAdded);
        });

        modelBuilder.Entity<Venue>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Notes)
                .HasMaxLength(1000);

            entity.Property(e => e.Address)
                .HasMaxLength(500);

            entity.Property(e => e.Phone)
                .HasMaxLength(20);

            entity.HasIndex(e => e.Name);
        });

        modelBuilder.Entity<KHostUser>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Notes)
                .HasMaxLength(1000);

            entity.Property(e => e.PasswordHash)
                .HasMaxLength(512);

            entity.HasIndex(e => e.Name).IsUnique();
            entity.HasIndex(e => e.CreatedDate);
            entity.HasIndex(e => e.IsAdmin);
        });

        modelBuilder.Entity<Performance>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.SingerId)
                .IsRequired();

            entity.Property(e => e.MediaId)
                .IsRequired();

            entity.Property(e => e.CreatedDate)
                .IsRequired();

            entity.HasIndex(e => e.SingerId);
            entity.HasIndex(e => e.MediaId);
            entity.HasIndex(e => e.CreatedDate);
        });
    }
}
