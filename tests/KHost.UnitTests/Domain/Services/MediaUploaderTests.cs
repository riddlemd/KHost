using KHost.Abstractions.Models;
using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Domain.Services;

namespace KHost.UnitTests.Domain.Services;

public class MediaUploaderTests : IDisposable
{
    private static readonly byte[] Picture = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"khost-media-upload-{Guid.NewGuid():N}");
    private readonly IMediaAcquisitionService _acquisition = Substitute.For<IMediaAcquisitionService>();
    private readonly IMediaFileParsingService _parser = Substitute.For<IMediaFileParsingService>();
    private readonly IMediaService _media = Substitute.For<IMediaService>();
    private readonly IMediaRepository _repository = Substitute.For<IMediaRepository>();
    private readonly MediaUploader _uploader;

    public MediaUploaderTests()
    {
        _acquisition.MediaDirectory.Returns(_root);
        _parser.LoadAndParseAsync(Arg.Any<string>(), Arg.Any<MediaType>())
            .Returns(ci => new Media { FilePath = ci.ArgAt<string>(0), Title = "parsed", Type = ci.ArgAt<MediaType>(1) });
        _media.CreateAsync(Arg.Any<Media>()).Returns(ci => ci.Arg<Media>());

        _uploader = new MediaUploader(_acquisition, _parser, _media, _repository);
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
        var added = await _uploader.AddAsync("Lounge logo.png", new MemoryStream(Picture), MediaType.Image);

        var path = Path.Combine(Images, "Lounge logo.png");
        Assert.Equal(path, added.FilePath);
        Assert.Equal(MediaType.Image, added.Type);
        Assert.Equal(Picture, File.ReadAllBytes(path));
        await _media.Received(1).CreateAsync(Arg.Is<Media>(m => m.FilePath == path));
    }

    [Fact]
    public async Task AddAsync_NotThatType_RefusesAndWritesNothing()
    {
        await Assert.ThrowsAsync<NotSupportedException>(
            () => _uploader.AddAsync("song.mp3", new MemoryStream(Picture), MediaType.Image));

        Assert.False(Directory.Exists(Images) && Directory.EnumerateFiles(Images).Any());
        await _media.DidNotReceiveWithAnyArgs().CreateAsync(default!);
    }

    /// <summary>The name comes from the browser; a folder in it must not steer the write.</summary>
    [Fact]
    public async Task AddAsync_ANameWithFolders_LandsInTheImagesFolderAnyway()
    {
        var added = await _uploader.AddAsync("../../escape.png", new MemoryStream(Picture), MediaType.Image);

        Assert.Equal(Path.Combine(Images, "escape.png"), added.FilePath);
    }

    /// <summary>Another picture under the same name may be on a venue's screen already.</summary>
    [Fact]
    public async Task AddAsync_ADifferentPictureHasTheName_KeepsItAndNumbersTheNewOne()
    {
        Directory.CreateDirectory(Images);
        File.WriteAllBytes(Path.Combine(Images, "logo.png"), [9, 9, 9]);

        var added = await _uploader.AddAsync("logo.png", new MemoryStream(Picture), MediaType.Image);

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

        var added = await _uploader.AddAsync("logo.png", new MemoryStream(Picture), MediaType.Image);

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

        var added = await _uploader.AddAsync("logo.png", new MemoryStream(Picture), MediaType.Image);

        Assert.Equal(path, added.FilePath);
        Assert.Single(Directory.EnumerateFiles(Images));
    }

    [Fact]
    public async Task AddAsync_TheRowCannotBeMade_RemovesTheCopy()
    {
        _media.CreateAsync(Arg.Any<Media>()).Returns<Media>(_ => throw new InvalidOperationException("db"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _uploader.AddAsync("logo.png", new MemoryStream(Picture), MediaType.Image));

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
            () => _uploader.AddAsync("logo.png", new MemoryStream(Picture), MediaType.Image));

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task AddAsync_AVideo_GoesToTheVideosFolderAsAVideo()
    {
        var added = await _uploader.AddAsync("intro.mp4", new MemoryStream(Picture), MediaType.Video);

        Assert.Equal(Path.Combine(_root, "Videos", "intro.mp4"), added.FilePath);
        Assert.Equal(MediaType.Video, added.Type);
        Assert.True(File.Exists(added.FilePath));
    }

    /// <summary>Karaoke is a pair or an archive, which one picked file cannot be.</summary>
    [Fact]
    public async Task AddAsync_AsKaraoke_Refuses()
        => await Assert.ThrowsAsync<NotSupportedException>(
            () => _uploader.AddAsync("song.mp4", new MemoryStream(Picture), MediaType.Karaoke));

    /// <summary>Same size is not same picture: the bytes decide.</summary>
    [Fact]
    public async Task AddAsync_ADifferentPictureOfTheSameSize_KeepsItAndNumbersTheNewOne()
    {
        Directory.CreateDirectory(Images);
        var other = Picture.Select(b => (byte)(b ^ 0xFF)).ToArray();
        File.WriteAllBytes(Path.Combine(Images, "logo.png"), other);
        _repository.FindByFilePathAsync(Arg.Any<string>()).Returns(new Media { FilePath = "x", Title = "x" });

        var added = await _uploader.AddAsync("logo.png", new MemoryStream(Picture), MediaType.Image);

        Assert.Equal(Path.Combine(Images, "logo (2).png"), added.FilePath);
        Assert.Equal(other, File.ReadAllBytes(Path.Combine(Images, "logo.png")));
    }

    /// <summary>A longer file that begins with the new one is a different picture, not a match.</summary>
    [Fact]
    public async Task AddAsync_ALongerPictureStartingTheSame_KeepsItAndNumbersTheNewOne()
    {
        Directory.CreateDirectory(Images);
        byte[] longer = [.. Picture, 0xAA];
        File.WriteAllBytes(Path.Combine(Images, "logo.png"), longer);

        var added = await _uploader.AddAsync("logo.png", new MemoryStream(Picture), MediaType.Image);

        Assert.Equal(Path.Combine(Images, "logo (2).png"), added.FilePath);
        Assert.Equal(longer, File.ReadAllBytes(Path.Combine(Images, "logo.png")));
    }

    /// <summary>The picture is written under a scratch name first; none may outlive the add.</summary>
    [Fact]
    public async Task AddAsync_EveryOutcome_LeavesNoPartialFileBehind()
    {
        await _uploader.AddAsync("logo.png", new MemoryStream(Picture), MediaType.Image);
        await _uploader.AddAsync("logo.png", new MemoryStream(Picture), MediaType.Image);
        _media.CreateAsync(Arg.Any<Media>()).Returns<Media>(_ => throw new InvalidOperationException("db"));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _uploader.AddAsync("other.png", new MemoryStream([1]), MediaType.Image));

        Assert.Empty(Directory.EnumerateFiles(Images, "*.part", new EnumerationOptions { AttributesToSkip = 0 }));
    }

    [Fact]
    public void ExtensionsFor_OffersOnlyWhatAPickedFileCanBeAddedAs()
    {
        var extensions = _uploader.ExtensionsFor([MediaType.Image, MediaType.Karaoke]);

        Assert.Contains(".png", extensions);
        Assert.DoesNotContain(".mp4", extensions);
        Assert.DoesNotContain(".cdg", extensions);
        Assert.Empty(_uploader.ExtensionsFor([MediaType.Karaoke]));
    }

    [Fact]
    public void ExtensionsFor_SeveralTypes_OffersEach()
    {
        var extensions = _uploader.ExtensionsFor([MediaType.Video, MediaType.Audio]);

        Assert.Contains(".mp4", extensions);
        Assert.Contains(".mp3", extensions);
        Assert.DoesNotContain(".png", extensions);
    }

    [Theory]
    [InlineData("intro.MP4", MediaType.Video)]
    [InlineData("bed.mp3", MediaType.Audio)]
    [InlineData("logo.png", null)]
    [InlineData("noextension", null)]
    public void TypeFor_AnswersFromTheNameAmongTheTypesAsked(string name, MediaType? expected)
        => Assert.Equal(expected, _uploader.TypeFor(name, [MediaType.Video, MediaType.Audio]));

    [Fact]
    public void MaxBytesFor_SizesEachKindAndRefusesKaraoke()
    {
        Assert.True(_uploader.MaxBytesFor(MediaType.Video) > _uploader.MaxBytesFor(MediaType.Image));
        Assert.True(_uploader.MaxBytesFor(MediaType.Image) > 0);
        Assert.Equal(0, _uploader.MaxBytesFor(MediaType.Karaoke));
    }
}
