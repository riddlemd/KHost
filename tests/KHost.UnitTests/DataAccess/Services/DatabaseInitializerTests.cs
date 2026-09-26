using KHost.Abstractions;
using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using KHost.Abstractions.Services;
using KHost.DataAccess.Repositories;
using KHost.DataAccess.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static KHost.DataAccess.Services.DatabaseInitializer;

namespace KHost.UnitTests.DataAccess.Services;

public class DatabaseInitializerTests
{
    private readonly IUsersService _usersService = Substitute.For<IUsersService>();
    private readonly IUserGroupsService _userGroupsService = Substitute.For<IUserGroupsService>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IVenuesService _venuesService = Substitute.For<IVenuesService>();
    private readonly IMediaService _mediaService = Substitute.For<IMediaService>();
    private readonly IMediaFileParsingService _mediaFileParsingService = Substitute.For<IMediaFileParsingService>();
    private readonly IOptionsMonitor<ServiceOptions> _optionsMonitor = Substitute.For<IOptionsMonitor<ServiceOptions>>();

    private DatabaseInitializer CreateSut(
        ServiceOptions options, IDbContextFactory<DefaultContext>? factory = null, ILogger<DatabaseInitializer>? logger = null)
    {
        _optionsMonitor.CurrentValue.Returns(options);
        return new DatabaseInitializer(
            factory!,
            logger ?? NullLogger<DatabaseInitializer>.Instance,
            _optionsMonitor,
            _usersService,
            _userGroupsService,
            _passwordHasher,
            _venuesService,
            _mediaService,
            _mediaFileParsingService);
    }

    [Fact]
    public async Task SeedDefaultAdminUserAsync_DoesNothing_WhenDefaultAdminUserIsNull()
    {
        var sut = CreateSut(new ServiceOptions { DefaultAdminUser = null });

        await sut.SeedDefaultAdminUserAsync();

        await _usersService.DidNotReceive().HasAdminUserAsync();
    }

    [Fact]
    public async Task SeedDefaultAdminUserAsync_DoesNothing_WhenAdminUserAlreadyExists()
    {
        _usersService.HasAdminUserAsync().Returns(true);
        var sut = CreateSut(new ServiceOptions
        {
            DefaultAdminUser = new ServiceOptions.DefaultAdminUserOptions { Username = "admin", Password = "pass" }
        });

        await sut.SeedDefaultAdminUserAsync();

        await _usersService.DidNotReceive().CreateAsync(Arg.Any<KHostUser>());
    }

    [Fact]
    public async Task SeedDefaultAdminUserAsync_CreatesAdminUser_WhenNoneExists()
    {
        _usersService.HasAdminUserAsync().Returns(false);
        _passwordHasher.HashAsync("pass").Returns("hashed");
        var created = new KHostUser { Id = Guid.NewGuid(), Name = "admin" };
        _usersService.CreateAsync(Arg.Any<KHostUser>()).Returns(created);

        var sut = CreateSut(new ServiceOptions
        {
            DefaultAdminUser = new ServiceOptions.DefaultAdminUserOptions { Username = "admin", Password = "pass" }
        });

        await sut.SeedDefaultAdminUserAsync();

        await _usersService.Received(1).CreateAsync(Arg.Is<KHostUser>(u => u.Name == "admin" && u.PasswordHash == "hashed"));
        await _userGroupsService.Received(1).AddUserToGroupAsync(created.Id, KHostUserGroup.AdminGroupId);
    }

    [Fact]
    public async Task SeedDefaultVenueAsync_DoesNothing_WhenDefaultVenueIsNull()
    {
        var sut = CreateSut(new ServiceOptions { DefaultVenue = null });

        await sut.SeedDefaultVenueAsync();

        await _venuesService.DidNotReceive().HasAnyAsync();
    }

    [Fact]
    public async Task SeedDefaultVenueAsync_DoesNothing_WhenVenueAlreadyExists()
    {
        _venuesService.HasAnyAsync().Returns(true);
        var sut = CreateSut(new ServiceOptions
        {
            DefaultVenue = new ServiceOptions.DefaultVenueOptions { Name = "Main Stage" }
        });

        await sut.SeedDefaultVenueAsync();

        await _venuesService.DidNotReceive().CreateAsync(Arg.Any<Venue>());
    }

    [Fact]
    public async Task SeedDefaultVenueAsync_CreatesAndSelectsVenue_WhenNoneExists()
    {
        _venuesService.HasAnyAsync().Returns(false);
        var created = new Venue { Id = Guid.NewGuid(), Name = "Main Stage" };
        _venuesService.CreateAsync(Arg.Any<Venue>()).Returns(created);

        var sut = CreateSut(new ServiceOptions
        {
            DefaultVenue = new ServiceOptions.DefaultVenueOptions { Name = "Main Stage" }
        });

        await sut.SeedDefaultVenueAsync();

        await _venuesService.Received(1).CreateAsync(Arg.Is<Venue>(v => v.Name == "Main Stage" && v.Enabled));
        await _venuesService.Received(1).SelectVenueAsync(created.Id);
    }

    [Fact]
    public async Task SeedDefaultMediaAsync_DoesNothing_WhenDefaultMediaIsNull()
    {
        var sut = CreateSut(new ServiceOptions { DefaultMedia = null });

        await sut.SeedDefaultMediaAsync();

        await _mediaService.DidNotReceive().HasAnyAsync();
    }

    [Fact]
    public async Task SeedDefaultMediaAsync_DoesNothing_WhenDefaultMediaIsEmpty()
    {
        var sut = CreateSut(new ServiceOptions { DefaultMedia = [] });

        await sut.SeedDefaultMediaAsync();

        await _mediaService.DidNotReceive().HasAnyAsync();
    }

    [Fact]
    public async Task SeedDefaultMediaAsync_DoesNothing_WhenMediaAlreadyExists()
    {
        _mediaService.HasAnyAsync().Returns(true);
        var sut = CreateSut(new ServiceOptions
        {
            DefaultMedia = [new ServiceOptions.DefaultMediaOptions { FilePath = "a.mp4" }]
        });

        await sut.SeedDefaultMediaAsync();

        await _mediaFileParsingService.DidNotReceive().LoadAndParseAsync(Arg.Any<string>());
    }

    /// <summary>Real files, since the seeder asks the disk whether each one is there.</summary>
    private static string TempMediaFile(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "khost-seed-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private static void DeleteTempMediaFile(string path)
    {
        try { Directory.Delete(Path.GetDirectoryName(path)!, recursive: true); } catch { /* scratch */ }
    }

    private static ServiceOptions Seeding(params string[] paths) => new()
    {
        DefaultMedia = [.. paths.Select(path => new ServiceOptions.DefaultMediaOptions { FilePath = path })],
    };

    [Fact]
    public async Task SeedDefaultMediaAsync_ParsesAndCreatesEachFile()
    {
        var a = TempMediaFile("a.mp4");
        var b = TempMediaFile("b.mp4");
        try
        {
            _mediaService.HasAnyAsync().Returns(false);
            var mediaA = new Media { Title = "A", FilePath = a };
            var mediaB = new Media { Title = "B", FilePath = b };
            _mediaFileParsingService.LoadAndParseAsync(a).Returns(mediaA);
            _mediaFileParsingService.LoadAndParseAsync(b).Returns(mediaB);
            _mediaService.CreateAsync(Arg.Any<Media>()).Returns(c => c.Arg<Media>());

            await CreateSut(Seeding(a, b)).SeedDefaultMediaAsync();

            await _mediaService.Received(1).CreateAsync(mediaA);
            await _mediaService.Received(1).CreateAsync(mediaB);
        }
        finally
        {
            DeleteTempMediaFile(a);
            DeleteTempMediaFile(b);
        }
    }

    [Fact]
    public async Task SeedDefaultMediaAsync_ContinuesAfterOneFileFailure()
    {
        var bad = TempMediaFile("bad.mp4");
        var good = TempMediaFile("good.mp4");
        try
        {
            _mediaService.HasAnyAsync().Returns(false);
            _mediaFileParsingService.LoadAndParseAsync(bad).Returns(Task.FromException<Media>(new InvalidOperationException("parse error")));
            var mediaB = new Media { Title = "B", FilePath = good };
            _mediaFileParsingService.LoadAndParseAsync(good).Returns(mediaB);
            _mediaService.CreateAsync(Arg.Any<Media>()).Returns(c => c.Arg<Media>());

            await CreateSut(Seeding(bad, good)).SeedDefaultMediaAsync();

            await _mediaService.Received(1).CreateAsync(mediaB);
        }
        finally
        {
            DeleteTempMediaFile(bad);
            DeleteTempMediaFile(good);
        }
    }

    /// <summary>The parser would answer a missing file with a Ready row and no duration, which
    /// the queue offers and the screens cannot play, so nothing is made for it.</summary>
    [Fact]
    public async Task SeedDefaultMediaAsync_AFileThatIsNotThere_IsNotSeeded()
    {
        var present = TempMediaFile("present.mp4");
        var missing = Path.Combine(Path.GetDirectoryName(present)!, "missing.mp4");
        try
        {
            _mediaService.HasAnyAsync().Returns(false);
            _mediaFileParsingService.LoadAndParseAsync(Arg.Any<string>())
                .Returns(c => new Media { Title = "T", FilePath = c.Arg<string>(), Status = MediaStatus.Ready });
            _mediaService.CreateAsync(Arg.Any<Media>()).Returns(c => c.Arg<Media>());
            var logger = new RecordingLogger();

            await CreateSut(Seeding(missing, present), logger: logger).SeedDefaultMediaAsync();

            await _mediaService.DidNotReceive().CreateAsync(Arg.Is<Media>(m => m.FilePath == missing));
            await _mediaService.Received(1).CreateAsync(Arg.Is<Media>(m => m.FilePath == present));
            Assert.Contains(logger.Entries, e => e.Level == LogLevel.Information && e.Message.Contains(missing, StringComparison.Ordinal));
        }
        finally { DeleteTempMediaFile(present); }
    }

    /// <summary>Seeding runs on every start; a library it already filled is left alone.</summary>
    [Fact]
    public async Task SeedDefaultMediaAsync_RunTwice_SeedsAnExistingFileOnce()
    {
        var path = TempMediaFile("once.mp4");
        try
        {
            var seeded = false;
            _mediaService.HasAnyAsync().Returns(_ => seeded);
            _mediaFileParsingService.LoadAndParseAsync(path).Returns(_ => new Media { Title = "Once", FilePath = path });
            _mediaService.CreateAsync(Arg.Any<Media>()).Returns(c => { seeded = true; return c.Arg<Media>(); });
            var sut = CreateSut(Seeding(path));

            await sut.SeedDefaultMediaAsync();
            await sut.SeedDefaultMediaAsync();

            await _mediaService.Received(1).CreateAsync(Arg.Is<Media>(m => m.FilePath == path));
        }
        finally { DeleteTempMediaFile(path); }
    }

    private sealed class RecordingLogger : ILogger<DatabaseInitializer>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    [Fact]
    public async Task SweepStalledDownloadsAsync_FlipsDownloadingRowsToBroken()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            SeedMedia(factory, ("stalled.mp4", MediaStatus.Downloading));
            var sut = CreateSut(new ServiceOptions(), factory);

            await sut.SweepStalledDownloadsAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(MediaStatus.Broken, context.Media.Single(m => m.FilePath == "stalled.mp4").Status);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    public async Task SweepStalledDownloadsAsync_FlipsProcessingRowsToBroken()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            // A row left mid-render is as stalled as one left mid-download: nothing survives the
            // process to finish it, and nothing else in the app can move it out of Processing.
            SeedMedia(factory, ("rendering.khv", MediaStatus.Processing));
            var sut = CreateSut(new ServiceOptions(), factory);

            await sut.SweepStalledDownloadsAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(MediaStatus.Broken, context.Media.Single(m => m.FilePath == "rendering.khv").Status);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    public async Task SweepStalledDownloadsAsync_NeverDeletesTheRow()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            SeedMedia(factory, ("stalled.mp4", MediaStatus.Downloading));
            var sut = CreateSut(new ServiceOptions(), factory);

            await sut.SweepStalledDownloadsAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(1, context.Media.Count(m => m.FilePath == "stalled.mp4"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    public async Task SweepStalledDownloadsAsync_LeavesReadyAndBrokenRowsAlone()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            SeedMedia(factory, ("ready.mp4", MediaStatus.Ready), ("broken.mp4", MediaStatus.Broken));
            var sut = CreateSut(new ServiceOptions(), factory);

            await sut.SweepStalledDownloadsAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(MediaStatus.Ready, context.Media.Single(m => m.FilePath == "ready.mp4").Status);
            Assert.Equal(MediaStatus.Broken, context.Media.Single(m => m.FilePath == "broken.mp4").Status);
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    public async Task SweepStalledDownloadsAsync_CountsRowsItSwept()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            SeedMedia(factory, ("a.mp4", MediaStatus.Downloading), ("b.mp4", MediaStatus.Downloading), ("c.mp4", MediaStatus.Ready));
            var sut = CreateSut(new ServiceOptions(), factory);

            await sut.SweepStalledDownloadsAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(2, context.Media.Count(m => m.Status == MediaStatus.Broken));
        }
        finally { Delete(dbPath); }
    }

    private static void SeedMedia(IDbContextFactory<DefaultContext> factory, params (string FilePath, MediaStatus Status)[] rows)
    {
        using var context = factory.CreateDbContext();
        foreach (var (filePath, status) in rows)
            context.Media.Add(new Media { FilePath = filePath, Title = filePath, Status = status });
        context.SaveChanges();
    }

    [Fact]
    public async Task RefoldStoredTextAsync_RepairsANameSqlCouldNotFold()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            // What the migration's lower() leaves behind for a non-ASCII name.
            Seed(factory, ("Ándre", "Ándre"), ("Steve", "steve"));
            var sut = CreateSut(new ServiceOptions(), factory);

            await sut.RefoldStoredTextAsync();

            Assert.Equal("andre", FoldedFor(factory, "Ándre"));
            Assert.Equal("steve", FoldedFor(factory, "Steve"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    public async Task RefoldStoredTextAsync_LeavesCorrectlyFoldedNamesAlone()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            Seed(factory, ("Steve", "steve"));
            var sut = CreateSut(new ServiceOptions(), factory);

            await sut.RefoldStoredTextAsync();

            Assert.Equal("steve", FoldedFor(factory, "Steve"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    public async Task RefoldStoredTextAsync_KeepsBothAccounts_WhenTwoLegacyNamesFoldTogether()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            // A pre-upgrade roster where the old, stricter fold let both of these exist.
            Seed(factory, ("Andre", "andre"));
            SeedRaw(factory, "Ándre", "ándre");
            var sut = CreateSut(new ServiceOptions(), factory);

            await sut.RefoldStoredTextAsync();

            // The collision cannot be repaired (the unique index rightly refuses), but nothing
            // may be lost: both rows survive, one of them still stale.
            using var context = factory.CreateDbContext();
            Assert.Equal(2, context.Users.Count());
            Assert.Equal("andre", context.Users.Single(u => u.Name == "Andre").NameFolded);

            var repository = new UsersRepository(factory, NullLogger<BaseRepository<KHostUser>>.Instance);
            Assert.Equal("Andre", (await repository.FindByNameAsync("andre"))?.Name);
            // The losing side stays reachable by its exact stored spelling.
            Assert.Equal("Ándre", (await repository.FindByNameAsync("Ándre"))?.Name);
        }
        finally { Delete(dbPath); }
    }

    // Bypasses the model entirely: the setter would fold the name and trip the unique index, which
    // is precisely what a legacy row predates.
    private static void SeedRaw(IDbContextFactory<DefaultContext> factory, string name, string folded)
    {
        using var context = factory.CreateDbContext();
        context.Database.ExecuteSqlRaw(
            "INSERT INTO Users (Id, Name, NameFolded, Notes, CreatedDate) VALUES ({0}, {1}, {2}, '', {3})",
            Guid.NewGuid().ToString().ToUpperInvariant(), name, folded, DateTime.UtcNow);
    }

    /// <summary>An ephemeral key must not outlive the process that issued it.</summary>
    [Fact]
    public async Task SweepEphemeralForeignKeysAsync_DropsEphemeralKeysAndKeepsDurableOnes()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            var ada = new KHostUser { Name = "Ada" };
            using (var seed = factory.CreateDbContext())
            {
                seed.Users.Add(ada);
                seed.UserForeignKeys.AddRange(
                    new KHostUserForeignKey { UserId = ada.Id, Source = "Example", Key = "remote-1", IsEphemeral = true },
                    new KHostUserForeignKey { UserId = ada.Id, Source = "Example", Key = "account-1", IsEphemeral = false });
                await seed.SaveChangesAsync();
            }

            var sut = CreateSut(new ServiceOptions(), factory);

            await sut.SweepEphemeralForeignKeysAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(["account-1"], context.UserForeignKeys.Select(k => k.Key).ToArray());
        }
        finally { Delete(dbPath); }
    }

    /// <summary>The singer stays; only the key naming their old connection goes.</summary>
    [Fact]
    public async Task SweepEphemeralForeignKeysAsync_NeverDeletesTheSinger()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            var ada = new KHostUser { Name = "Ada" };
            using (var seed = factory.CreateDbContext())
            {
                seed.Users.Add(ada);
                seed.UserForeignKeys.Add(
                    new KHostUserForeignKey { UserId = ada.Id, Source = "Example", Key = "remote-1", IsEphemeral = true });
                await seed.SaveChangesAsync();
            }

            var sut = CreateSut(new ServiceOptions(), factory);

            await sut.SweepEphemeralForeignKeysAsync();

            using var context = factory.CreateDbContext();
            Assert.Equal(1, context.Users.Count(u => u.Name == "Ada"));
        }
        finally { Delete(dbPath); }
    }

    private static (IDbContextFactory<DefaultContext> Factory, string Path) NewDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"khost-refold-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<DefaultContext>(o =>
            o.UseSqlite($"Data Source={path}").UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        var factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<DefaultContext>>();
        using (var context = factory.CreateDbContext()) context.Database.Migrate();
        return (factory, path);
    }

    // Inserts through EF so keys are stored the way EF writes them, then overwrites just the folded
    // column to put the row in the state the migration's lower() leaves it in.
    private static void Seed(IDbContextFactory<DefaultContext> factory, params (string Name, string Folded)[] users)
    {
        using var context = factory.CreateDbContext();
        foreach (var (name, folded) in users)
        {
            var user = new KHostUser { Name = name };
            context.Users.Add(user);
            context.SaveChanges();
            context.Database.ExecuteSqlRaw("UPDATE Users SET NameFolded = {0} WHERE Id = {1}", folded, user.Id);
        }
    }

    private static string FoldedFor(IDbContextFactory<DefaultContext> factory, string name)
    {
        using var context = factory.CreateDbContext();
        return context.Users.Single(u => u.Name == name).NameFolded;
    }

    private static void Delete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
