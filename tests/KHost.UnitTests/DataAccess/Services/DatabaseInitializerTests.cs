using KHost.Abstractions;
using KHost.Abstractions.Models;
using KHost.DataAccess.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using KHost.Abstractions.Services;
using KHost.DataAccess.Services;
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

    private DatabaseInitializer CreateSut(ServiceOptions options, IDbContextFactory<DefaultContext>? factory = null)
    {
        _optionsMonitor.CurrentValue.Returns(options);
        return new DatabaseInitializer(
            factory!,
            NullLogger<DatabaseInitializer>.Instance,
            _optionsMonitor,
            _usersService,
            _userGroupsService,
            _passwordHasher,
            _venuesService,
            _mediaService,
            _mediaFileParsingService);
    }

    // --- SeedDefaultAdminUserAsync ---

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

    // --- SeedDefaultVenueAsync ---

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

    // --- SeedDefaultMediaAsync ---

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

    [Fact]
    public async Task SeedDefaultMediaAsync_ParsesAndCreatesEachFile()
    {
        _mediaService.HasAnyAsync().Returns(false);
        var mediaA = new Media { Title = "A", FilePath = "a.mp4" };
        var mediaB = new Media { Title = "B", FilePath = "b.mp4" };
        _mediaFileParsingService.LoadAndParseAsync("a.mp4").Returns(mediaA);
        _mediaFileParsingService.LoadAndParseAsync("b.mp4").Returns(mediaB);
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(c => c.Arg<Media>());

        var sut = CreateSut(new ServiceOptions
        {
            DefaultMedia =
            [
                new ServiceOptions.DefaultMediaOptions { FilePath = "a.mp4" },
                new ServiceOptions.DefaultMediaOptions { FilePath = "b.mp4" },
            ]
        });

        await sut.SeedDefaultMediaAsync();

        await _mediaService.Received(1).CreateAsync(mediaA);
        await _mediaService.Received(1).CreateAsync(mediaB);
    }

    [Fact]
    public async Task SeedDefaultMediaAsync_ContinuesAfterOneFileFailure()
    {
        _mediaService.HasAnyAsync().Returns(false);
        _mediaFileParsingService.LoadAndParseAsync("bad.mp4").Returns(Task.FromException<Media>(new Exception("parse error")));
        var mediaB = new Media { Title = "B", FilePath = "good.mp4" };
        _mediaFileParsingService.LoadAndParseAsync("good.mp4").Returns(mediaB);
        _mediaService.CreateAsync(Arg.Any<Media>()).Returns(c => c.Arg<Media>());

        var sut = CreateSut(new ServiceOptions
        {
            DefaultMedia =
            [
                new ServiceOptions.DefaultMediaOptions { FilePath = "bad.mp4" },
                new ServiceOptions.DefaultMediaOptions { FilePath = "good.mp4" },
            ]
        });

        await sut.SeedDefaultMediaAsync();

        await _mediaService.Received(1).CreateAsync(mediaB);
    }

    // --- RefoldUserNamesAsync ---

    [Fact]
    public async Task RefoldUserNamesAsync_RepairsANameSqlCouldNotFold()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            // What the migration's lower() leaves behind for a non-ASCII name.
            Seed(factory, ("Ándre", "Ándre"), ("Steve", "steve"));
            var sut = CreateSut(new ServiceOptions(), factory);

            await sut.RefoldUserNamesAsync();

            Assert.Equal("ándre", FoldedFor(factory, "Ándre"));
            Assert.Equal("steve", FoldedFor(factory, "Steve"));
        }
        finally { Delete(dbPath); }
    }

    [Fact]
    public async Task RefoldUserNamesAsync_LeavesCorrectlyFoldedNamesAlone()
    {
        var (factory, dbPath) = NewDatabase();
        try
        {
            Seed(factory, ("Steve", "steve"));
            var sut = CreateSut(new ServiceOptions(), factory);

            await sut.RefoldUserNamesAsync();

            Assert.Equal("steve", FoldedFor(factory, "Steve"));
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
