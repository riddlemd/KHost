using KHost.Abstractions.Repositories;
using KHost.Abstractions.Services;
using KHost.Abstractions.Messaging;
using KHost.Abstractions.Messaging.Messages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using System.Collections.Concurrent;

namespace KHost.UserInterface.Components;

public partial class MediaBrowser : IAsyncDisposable
{
    private enum SortColumn { Name, Type, Status, Size, Modified }

    [Inject] private IMediaImportService? ImportService { get; set; }
    [Inject] private IMediaRepository? MediaRepository { get; set; }
    [Inject] private IMediaFileParsingService? ParsingService { get; set; }
    [Inject] private IMessageBroker Broker { get; set; } = default!;

    private readonly SubscriptionSet _subscriptions = new();

    [Parameter]
    public EventCallback OnImportCompleted { get; set; }

    private string? _currentPath;
    private string _pathInput = string.Empty;
    private string _filterQuery = string.Empty;
    private bool _hideImported = false;
    private SortColumn _sortColumn = SortColumn.Name;
    private bool _sortAsc = true;
    private bool _breadcrumbEditMode = false;
    private readonly ConcurrentDictionary<string, (string Title, string? Artist)> _parsedMetadataCache = new(StringComparer.OrdinalIgnoreCase);
    private List<FileEntry> _entries = [];
    private readonly HashSet<string> _selectedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedFolderPaths = new(StringComparer.OrdinalIgnoreCase);
    private ImportState _prevImportState = ImportState.Idle;

    private List<(string Display, string FullPath)> GetBreadcrumbSegments()
    {
        if (_currentPath is null)
            return [];

        var segments = new List<(string Display, string FullPath)>();
        var path = _currentPath;

        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root))
        {
            segments.Add((root.TrimEnd('\\'), root.TrimEnd('\\')));
            path = path[root.Length..];
        }

        var parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var currentPath = root ?? string.Empty;

        foreach (var part in parts)
        {
            currentPath = Path.Combine(currentPath, part);
            segments.Add((part, currentPath));
        }

        return segments;
    }

    private bool HasNewFiles => FilteredEntries.Any(e => !e.IsDirectory && !e.AlreadyImported);

    private bool HasSelectableFolders => FilteredEntries.Any(e => e.IsDirectory && e.Name != "..");

    private List<FileEntry> FilteredEntries
    {
        get
        {
            var filtered = _entries.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_filterQuery))
            {
                filtered = filtered
                    .Where(e => e.Name == ".." || e.Name.Contains(_filterQuery, StringComparison.OrdinalIgnoreCase));
            }

            if (_hideImported)
            {
                filtered = filtered
                    .Where(e => e.IsDirectory || !e.AlreadyImported);
            }

            var parentEntry = filtered.FirstOrDefault(e => e.Name == "..");
            var folders = filtered.Where(e => e.IsDirectory && e.Name != "..").ToList();
            var files = filtered.Where(e => !e.IsDirectory).ToList();

            folders = _sortColumn switch
            {
                SortColumn.Name => _sortAsc ? folders.OrderBy(e => e.Name).ToList() : folders.OrderByDescending(e => e.Name).ToList(),
                SortColumn.Type => folders,
                SortColumn.Status => folders,
                SortColumn.Size => folders,
                SortColumn.Modified => _sortAsc ? folders.OrderBy(e => e.ModifiedDate).ToList() : folders.OrderByDescending(e => e.ModifiedDate).ToList(),
                _ => folders
            };

            files = _sortColumn switch
            {
                SortColumn.Name => _sortAsc ? files.OrderBy(e => e.Name).ToList() : files.OrderByDescending(e => e.Name).ToList(),
                SortColumn.Type => _sortAsc ? files.OrderBy(e => e.Extension).ToList() : files.OrderByDescending(e => e.Extension).ToList(),
                SortColumn.Status => _sortAsc ? files.OrderBy(e => e.AlreadyImported).ToList() : files.OrderByDescending(e => e.AlreadyImported).ToList(),
                SortColumn.Size => _sortAsc ? files.OrderBy(e => e.Size).ToList() : files.OrderByDescending(e => e.Size).ToList(),
                SortColumn.Modified => _sortAsc ? files.OrderBy(e => e.ModifiedDate).ToList() : files.OrderByDescending(e => e.ModifiedDate).ToList(),
                _ => files
            };

            var result = new List<FileEntry>();
            if (parentEntry is not null)
                result.Add(parentEntry);
            result.AddRange(folders);
            result.AddRange(files);

            return result;
        }
    }

    private bool AllNewSelected
    {
        get
        {
            var files = FilteredEntries.Where(e => !e.IsDirectory && !e.AlreadyImported).ToList();
            var folders = FilteredEntries.Where(e => e.IsDirectory && e.Name != "..").ToList();
            if (files.Count == 0 && folders.Count == 0) return false;
            return files.All(e => _selectedPaths.Contains(e.FullPath))
                && folders.All(e => _selectedFolderPaths.Contains(e.FullPath));
        }
    }

    private string SelectAllIconName
    {
        get
        {
            if (AllNewSelected) return "check-square";
            bool anyCurrentFolderSelected = FilteredEntries.Any(e =>
                (!e.IsDirectory && _selectedPaths.Contains(e.FullPath)) ||
                (e.IsDirectory && e.Name != ".." && _selectedFolderPaths.Contains(e.FullPath)));
            if (anyCurrentFolderSelected) return "slash-square";
            return "square";
        }
    }

    protected override async Task OnInitializedAsync()
    {
        _subscriptions.Add(Broker.Subscribe<MediaImportChanged>(OnImportStateChanged));

        var musicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (Directory.Exists(musicPath))
            await NavigateToAsync(musicPath);
        else
            await LoadDrivesAsync();
    }

    private Task LoadDrivesAsync()
    {
        _currentPath = null;
        _pathInput = string.Empty;
        _filterQuery = string.Empty;
        _entries = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => new FileEntry(d.RootDirectory.FullName, d.Name.TrimEnd('\\'), true, string.Empty, false, 0, DateTime.MinValue, null, null, null, null))
            .ToList();
        return Task.CompletedTask;
    }

    private async Task NavigateToAsync(string path)
    {
        _currentPath = path;
        _pathInput = path;
        _filterQuery = string.Empty;

        List<string> filePaths;
        List<FileEntry> dirs;

        try
        {
            var supportedExts = ImportService!.SupportedExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);

            dirs = Directory.GetDirectories(path)
                .Select(d =>
                {
                    int fileCount = 0;
                    try
                    {
                        fileCount = Directory.EnumerateFiles(d)
                            .Count(f => supportedExts.Contains(Path.GetExtension(f)));
                    }
                    catch { }

                    return new FileEntry(
                        d,
                        Path.GetFileName(d),
                        true,
                        string.Empty,
                        false,
                        0,
                        Directory.GetLastWriteTime(d),
                        fileCount > 0 ? fileCount : null,
                        null,
                        null,
                        null);
                })
                .OrderBy(e => e.Name)
                .ToList();

            filePaths = Directory.GetFiles(path)
                .Where(f => supportedExts.Contains(Path.GetExtension(f)))
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            _entries = [];
            return;
        }

        var existingInDb = await MediaRepository!.GetExistingFilePathsAsync(filePaths);

        var files = filePaths
            .Select(f =>
            {
                var fileInfo = new FileInfo(f);
                return new FileEntry(
                    f,
                    Path.GetFileName(f),
                    false,
                    Path.GetExtension(f).TrimStart('.'),
                    existingInDb.Contains(f),
                    fileInfo.Length,
                    fileInfo.LastWriteTime,
                    null,
                    null,
                    null,
                    null);
            })
            .OrderBy(e => e.Name)
            .ToList();

        var parent = Directory.GetParent(path);
        var parentEntry = new FileEntry(parent?.FullName ?? string.Empty, "..", true, string.Empty, false, 0, DateTime.MinValue, null, null, null, null);

        files = GroupKaraokePairs(files);

        _entries = [parentEntry, ..dirs, ..files];
    }

    private async Task GoUpAsync()
    {
        if (_currentPath is null)
            return;

        var parent = Directory.GetParent(_currentPath);
        if (parent is null)
            await LoadDrivesAsync();
        else
            await NavigateToAsync(parent.FullName);
    }

    internal static List<FileEntry> GroupKaraokePairs(List<FileEntry> files)
    {
        var result = new List<FileEntry>();
        var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var groups = files
            .GroupBy(f => Path.GetFileNameWithoutExtension(f.FullPath), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groups)
        {
            var groupFiles = group.ToList();

            var cdgFile = groupFiles.FirstOrDefault(f => f.Extension.Equals("cdg", StringComparison.OrdinalIgnoreCase));

            // .mp3 only: CD+G rips have always shipped that way, so a same-named file in another
            // format is a different track and keeps its own row rather than joining the pair.
            var mp3File = groupFiles.FirstOrDefault(f => f.Extension.Equals("mp3", StringComparison.OrdinalIgnoreCase));

            if (cdgFile is not null && mp3File is not null)
            {
                result.Add(cdgFile with
                {
                    Name = $"{group.Key} (CDG + MP3)",
                    PairedPaths = [cdgFile.FullPath, mp3File.FullPath],
                });

                processedPaths.Add(cdgFile.FullPath);
                processedPaths.Add(mp3File.FullPath);
            }

            foreach (var file in groupFiles)
            {
                if (processedPaths.Add(file.FullPath))
                    result.Add(file);
            }
        }

        return result;
    }

    private async Task RefreshAsync()
    {
        if (_currentPath is null)
            await LoadDrivesAsync();
        else
            await NavigateToAsync(_currentPath);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private string GetParsedMetadataTooltip(FileEntry entry)
    {
        if (entry.IsDirectory)
            return entry.SupportedFileCount is not null ? $"{entry.Name} ({entry.SupportedFileCount})" : entry.Name;

        if (_parsedMetadataCache.TryGetValue(entry.FullPath, out var cached))
            return $"{cached.Title} — {cached.Artist ?? "Unknown"}";

        _ = Task.Run(async () =>
        {
            if (ParsingService is not null && !_parsedMetadataCache.ContainsKey(entry.FullPath))
            {
                try
                {
                    var (title, artist) = ParsingService.GetTitleAndArtistFromFilename(entry.FullPath);
                    _parsedMetadataCache.TryAdd(entry.FullPath, (title, artist));
                    await InvokeAsync(StateHasChanged);
                }
                catch { }
            }
        });

        return entry.Name;
    }

    private void OnSortColumnClicked(SortColumn column)
    {
        if (_sortColumn == column)
            _sortAsc = !_sortAsc;
        else
        {
            _sortColumn = column;
            _sortAsc = true;
        }
    }

    private async Task OnPathInputKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key != "Enter" || string.IsNullOrWhiteSpace(_pathInput))
            return;

        try
        {
            if (Directory.Exists(_pathInput))
                await NavigateToAsync(_pathInput);
            else
                _pathInput = _currentPath ?? string.Empty;
        }
        catch
        {
            _pathInput = _currentPath ?? string.Empty;
        }
    }

    private async Task OnEntryClickAsync(FileEntry entry)
    {
        if (entry.IsDirectory)
        {
            if (string.IsNullOrEmpty(entry.FullPath))
                await LoadDrivesAsync();
            else
                await NavigateToAsync(entry.FullPath);
        }
        else if (!entry.AlreadyImported)
            ToggleSelection(entry);
    }

    private void ToggleFolderSelection(FileEntry entry)
    {
        if (!_selectedFolderPaths.Remove(entry.FullPath))
            _selectedFolderPaths.Add(entry.FullPath);
    }

    private void ToggleSelection(FileEntry entry)
    {
        if (entry.AlreadyImported)
            return;

        var pathsToToggle = entry.PairedPaths ?? new List<string> { entry.FullPath };

        bool allSelected = pathsToToggle.All(p => _selectedPaths.Contains(p));

        if (allSelected)
        {
            foreach (var path in pathsToToggle)
                _selectedPaths.Remove(path);
        }
        else
        {
            foreach (var path in pathsToToggle)
                _selectedPaths.Add(path);
        }
    }

    private void OnSelectAllClicked()
    {
        if (AllNewSelected)
        {
            foreach (var entry in FilteredEntries.Where(e => !e.IsDirectory))
                foreach (var path in (entry.PairedPaths ?? [entry.FullPath]))
                    _selectedPaths.Remove(path);
            foreach (var entry in FilteredEntries.Where(e => e.IsDirectory && e.Name != ".."))
                _selectedFolderPaths.Remove(entry.FullPath);
        }
        else
        {
            foreach (var entry in FilteredEntries.Where(e => !e.IsDirectory && !e.AlreadyImported))
                _selectedPaths.Add(entry.FullPath);
            foreach (var entry in FilteredEntries.Where(e => e.IsDirectory && e.Name != ".."))
                _selectedFolderPaths.Add(entry.FullPath);
        }
    }

    private async Task StartImportAsync()
    {
        if (_selectedPaths.Count == 0 && _selectedFolderPaths.Count == 0)
            return;

        var filePaths = new HashSet<string>(_selectedPaths, StringComparer.OrdinalIgnoreCase);
        _selectedPaths.Clear();

        var folderPaths = _selectedFolderPaths.ToList();
        _selectedFolderPaths.Clear();

        if (folderPaths.Count > 0)
        {
            var supportedExts = ImportService!.SupportedExtensions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var folderFiles = await Task.Run(() =>
            {
                var results = new List<string>();
                foreach (var folder in folderPaths)
                {
                    try
                    {
                        results.AddRange(
                            Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                                .Where(f => supportedExts.Contains(Path.GetExtension(f))));
                    }
                    catch { }
                }
                return results;
            });

            foreach (var f in folderFiles)
                filePaths.Add(f);
        }

        await ImportService!.StartAsync(filePaths);
    }

    private void OnImportStateChanged(MediaImportChanged message) =>
        _ = InvokeAsync(async () =>
        {
            var state = ImportService!.State;

            if (_prevImportState != ImportState.Idle && state == ImportState.Idle && _currentPath is not null)
            {
                _prevImportState = state;
                await NavigateToAsync(_currentPath);
                await OnImportCompleted.InvokeAsync();
            }
            else
            {
                _prevImportState = state;
            }

            StateHasChanged();
        });

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _subscriptions.Dispose();

        await Task.CompletedTask;
    }

    internal sealed record FileEntry(
        string FullPath,
        string Name,
        bool IsDirectory,
        string Extension,
        bool AlreadyImported,
        long Size,
        DateTime ModifiedDate,
        int? SupportedFileCount,
        List<string>? PairedPaths,
        string? ParsedTitle,
        string? ParsedArtist);
}
