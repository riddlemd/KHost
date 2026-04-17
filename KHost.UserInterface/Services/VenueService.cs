using KHost.UserInterface.Models;
using System.Text.Json;

namespace KHost.UserInterface.Services;

public class VenueService : IDisposable
{
    private readonly List<Venue> _venues = [];
    private readonly string _filePath;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private bool _disposed;

    public event Action? StateChanged;

    public IReadOnlyList<Venue> Venues => _venues;
    public Guid? SelectedVenueId { get; private set; }
    public Venue? SelectedVenue =>
        SelectedVenueId is { } id ? _venues.FirstOrDefault(v => v.Id == id) : null;

    public VenueService(IWebHostEnvironment env)
    {
        _filePath = Path.Combine(env.ContentRootPath, "venues.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var venues = JsonSerializer.Deserialize<List<Venue>>(json, JsonSerializerOptions.Web);
                if (venues != null)
                {
                    _venues.Clear();
                    _venues.AddRange(venues);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading venues from file: {ex.Message}");
        }
    }

    private async Task SaveAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var json = JsonSerializer.Serialize(_venues, new JsonSerializerOptions { WriteIndented = true });
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving venues to file: {ex.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void Notify()
    {
        _ = SaveAsync();
        StateChanged?.Invoke();
    }

    public Venue AddVenue(string name)
    {
        var venue = new Venue { Name = name };
        _venues.Add(venue);
        Notify();
        return venue;
    }

    public void UpdateVenue(Guid venueId, string name, string notes = "", bool enabled = true)
    {
        var venue = _venues.FirstOrDefault(v => v.Id == venueId);
        if (venue is null) return;
        venue.Name = name;
        venue.Notes = notes;
        venue.Enabled = enabled;
        venue.LastUpdated = DateTime.UtcNow;
        Notify();
    }

    public void RemoveVenue(Guid venueId)
    {
        _venues.RemoveAll(v => v.Id == venueId);
        Notify();
    }

    public List<Venue> SearchVenues(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return _venues.ToList();

        var lowerQuery = query.ToLowerInvariant();
        return _venues
            .Where(v => v.Name.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase) ||
                       v.Notes.Contains(lowerQuery, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public Venue? GetVenueById(Guid venueId) =>
        _venues.FirstOrDefault(v => v.Id == venueId);

    public Venue? GetVenueByName(string name) =>
        _venues.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void SelectVenue(Guid? venueId)
    {
        SelectedVenueId = venueId;
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _saveLock?.Dispose();
            _disposed = true;
        }
    }
}
