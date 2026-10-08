using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class ImageUploaderTests : IDisposable
{
    private static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"khost-image-upload-{Guid.NewGuid():N}");
    private readonly IMediaAcquisitionService _acquisition = Substitute.For<IMediaAcquisitionService>();
    private readonly IMediaFileParsingService _parser = Substitute.For<IMediaFileParsingService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly ImageUploader _uploader;

    public ImageUploaderTests()
    {
        _acquisition.MediaDirectory.Returns(_root);
        _parser.LoadAndParseAsync(Arg.Any<string>(), Arg.Any<MediaType>())
            .Returns(ci => new Media { FilePath = ci.ArgAt<string>(0), Title = "parsed", Type = ci.ArgAt<MediaType>(1) });
        _media.CreateAsync(Arg.Any<Media>()).Returns(ci => ci.Arg<Media>());

        _uploader = new ImageUploader(_acquisition, _parser, _media, _repository);
    }

    private string Images => Path.Combine(_root, "Images");

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task AddAsync_APicture_CopiesItIntoTheMediaFolderAndAddsItAsAnImage()
    {
        var added = await _uploader.AddAsync("Lounge logo.png", new MemoryStream(Picture));

        var path = Path.Combine(Images, "Lounge logo.png");
        Assert.Equal(path, added.FilePath);
        Assert.Equal(MediaType.Image, added.Type);
        Assert.Equal(Picture, File.ReadAllBytes(path));
        await _media.Received(1).CreateAsync(Arg.Is<Media>(m => m.FilePath == path));
    }

    [Fact]
    public async Task AddAsync_NotAPicture_RefusesAndWritesNothing()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _uploader.AddAsync("song.mp3", new MemoryStream(Picture)));

        Assert.False(Directory.Exists(Images) && Directory.EnumerateFiles(Images).Any());
        await _media.DidNotReceiveWithAnyArgs().CreateAsync(default!);
    }

    /// <summary>The name comes from the browser; a folder in it must not steer the write.</summary>
    [Fact]
    public async Task AddAsync_ANameWithFolders_LandsInTheImagesFolderAnyway()
    {
        var added = await _uploader.AddAsync("../../escape.png", new MemoryStream(Picture));

        Assert.Equal(Path.Combine(Images, "escape.png"), added.FilePath);
    }

    /// <summary>Another picture under the same name may be on a venue's screen already.</summary>
    [Fact]
    public async Task AddAsync_ADifferentPictureHasTheName_KeepsItAndNumbersTheNewOne()
    {
        Directory.CreateDirectory(Images);
        File.WriteAllBytes(Path.Combine(Images, "logo.png"), [9, 9, 9]);

        var added = await _uploader.AddAsync("logo.png", new MemoryStream(Picture));

        Assert.Equal(Path.Combine(Images, "logo (2).png"), added.FilePath);
        Assert.Equal([9, 9, 9], File.ReadAllBytes(Path.Combine(Images, "logo.png")));
        Assert.Equal(Picture, File.ReadAllBytes(added.FilePath));
    }

    [Fact]
    public async Task AddAsync_TheSamePicturePickedAgain_ReturnsItsRowWithoutACopy()
    {
        Directory.CreateDirectory(Images);
        var path = Path.Combine(Images, "logo.png");
        File.WriteAllBytes(path, Picture);
        var row = new Media { FilePath = path, Title = "logo", Type = MediaType.Image };
        _repository.FindByFilePathAsync(path).Returns(row);

        var added = await _uploader.AddAsync("logo.png", new MemoryStream(Picture));

        Assert.Same(row, added);
        Assert.Single(Directory.EnumerateFiles(Images));
        await _media.DidNotReceiveWithAnyArgs().CreateAsync(default!);
    }

    /// <summary>A copy left behind by a failed add is still the picture; it gets its row now.</summary>
    [Fact]
    public async Task AddAsync_TheSamePictureOnDiskWithNoRow_AddsThatFile()
    {
        Directory.CreateDirectory(Images);
        var path = Path.Combine(Images, "logo.png");
        File.WriteAllBytes(path, Picture);

        var added = await _uploader.AddAsync("logo.png", new MemoryStream(Picture));

        Assert.Equal(path, added.FilePath);
        Assert.Single(Directory.EnumerateFiles(Images));
    }

    [Fact]
    public async Task AddAsync_TheRowCannotBeMade_RemovesTheCopy()
    {
        _media.CreateAsync(Arg.Any<Media>()).Returns<Media>(_ => throw new InvalidOperationException("db"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _uploader.AddAsync("logo.png", new MemoryStream(Picture)));

        Assert.Empty(Directory.EnumerateFiles(Images));
    }

    /// <summary>The file was there before this add; failing to make its row is no reason to delete it.</summary>
    [Fact]
    public async Task AddAsync_TheRowCannotBeMadeForAFileAlreadyThere_LeavesTheFile()
    {
        Directory.CreateDirectory(Images);
        var path = Path.Combine(Images, "logo.png");
        File.WriteAllBytes(path, Picture);
        _media.CreateAsync(Arg.Any<Media>()).Returns<Media>(_ => throw new InvalidOperationException("db"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _uploader.AddAsync("logo.png", new MemoryStream(Picture)));

        Assert.True(File.Exists(path));
    }
}
