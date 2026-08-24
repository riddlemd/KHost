using System.Text.Json;
using KHost.Abstractions.Models;
using KHost.DataAccess.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace KHost.DataAccess.Contexts;

internal class DefaultContext : DbContext
{
    public DbSet<Media> Media { get; set; }
    public DbSet<Venue> Venues { get; set; }
    public DbSet<KHostUser> Users { get; set; }
    public DbSet<KHostUserGroup> UserGroups { get; set; }
    public DbSet<Performance> Performances { get; set; }
    public DbSet<Tip> Tips { get; set; }

    public DefaultContext(DbContextOptions<DefaultContext> options) : base(options)
    {

    }

    // Both overloads, because callers use each: EF does not route one through the other.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EntityFolding.Apply(ChangeTracker);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EntityFolding.Apply(ChangeTracker);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
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

            entity.Property(e => e.SearchFolded)
                .IsRequired()
                .HasMaxLength(600);

            entity.Property(e => e.SampledHash)
                .HasMaxLength(64);

            entity.Property(e => e.ContentHash)
                .HasMaxLength(64);

            entity.HasIndex(e => e.FilePath).IsUnique();
            entity.HasIndex(e => e.Title);
            entity.HasIndex(e => e.Artist);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.DateAdded);

            // Every listing and search filters on kind, so it sits ahead of the sort columns
            // rather than beside them.
            entity.HasIndex(e => e.Kind);

            // Import dedup looks rows up by size first; neither hash is unique, because the same
            // file legitimately exists under two paths until one of them is skipped.
            entity.HasIndex(e => e.FileSize);
            entity.HasIndex(e => e.ContentHash);
        });

        modelBuilder.Entity<Venue>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.NameFolded)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Notes)
                .HasMaxLength(1000);

            entity.Property(e => e.Address)
                .HasMaxLength(500);

            entity.Property(e => e.Phone)
                .HasMaxLength(20);

            entity.OwnsOne(e => e.Settings, settings =>
            {
                settings.ToJson();
                settings.OwnsOne(s => s.QueueRotation);
            });

            entity.HasIndex(e => e.Name)
                .IsUnique();
        });

        modelBuilder.Entity<KHostUser>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.NameFolded)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Notes)
                .HasMaxLength(1000);

            entity.Property(e => e.PasswordHash)
                .HasMaxLength(512);

            // Uniqueness is on the folded name, not on Name COLLATE NOCASE: NOCASE folds ASCII
            // only, so it let "ZOË" and "zoë" both exist as separate singers.
            entity.HasIndex(e => e.NameFolded).IsUnique();
            entity.HasIndex(e => e.CreatedDate);
        });

        modelBuilder.Entity<KHostUser>()
            .HasMany(u => u.Groups)
            .WithMany(g => g.Users)
            .UsingEntity<UserGroupMembership>(
                l => l.HasOne<KHostUserGroup>().WithMany().HasForeignKey(m => m.GroupId),
                r => r.HasOne<KHostUser>().WithMany().HasForeignKey(m => m.UserId),
                j =>
                {
                    j.HasKey(m => new { m.GroupId, m.UserId });
                    j.ToTable("UserGroupMemberships");
                });

        modelBuilder.Entity<KHostUser>()
            .HasMany(u => u.Tips)
            .WithOne()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<KHostUserGroup>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.NameFolded)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Description)
                .HasMaxLength(500);

            entity.HasIndex(e => e.Name).IsUnique();

            entity.Property(e => e.Permissions)
                .HasColumnType("JSON");

            entity.Property(e => e.IsAdmin)
                .HasDefaultValue(false);

            entity.Property(e => e.ExcludeFromSingerQueue)
                .HasDefaultValue(false);

            entity.HasData(
                new KHostUserGroup
                {
                    Id = new Guid("00000000-0000-0000-0000-000000000001"),
                    Name = "Admin",
                    NameFolded = "admin",
                    Description = "Full access to all management features",
                    IsAdmin = true,
                    // The admin account is how a host signs in, not somebody waiting to sing.
                    ExcludeFromSingerQueue = true,
                    Permissions = []
                },
                new KHostUserGroup
                {
                    Id = new Guid("00000000-0000-0000-0000-000000000002"),
                    Name = "Regular",
                    NameFolded = "regular",
                    Description = "Frequent singer",
                    IsAdmin = false,
                    ExcludeFromSingerQueue = false,
                    Permissions = []
                }
            );
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

            // Indexed, deliberately without foreign keys: deleting a song, a singer or a venue
            // must leave the record of who sang what standing rather than cascade it away.
            entity.HasIndex(e => e.SingerId);
            entity.HasIndex(e => e.MediaId);
            entity.HasIndex(e => e.VenueId);
            entity.HasIndex(e => e.CreatedDate);
        });

        modelBuilder.Entity<Tip>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.UserId)
                .IsRequired();

            entity.Property(e => e.NotesFolded)
                .IsRequired()
                .HasMaxLength(1000);

            entity.Property(e => e.Notes)
                .HasMaxLength(1000);

            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.VenueId);
            entity.HasIndex(e => e.CreatedDate);
        });
    }
}
