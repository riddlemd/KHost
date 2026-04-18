using KHost.Abstractions.Models;
using KHost.Abstractions.Services;
using KHost.Domain.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace KHost.Domain.Services;

public class VenuesService : IVenuesService
{
    private const string _cacheKey = "venues";
    private const int _defaultPageSize = 50;
    private const int _maxPageSize = 1000;

    private readonly ICacheService _cacheService;
    private readonly List<Venue> _venues = [];

    public event Action? StateChanged;

    public IReadOnlyList<IVenue> Venues => _venues.AsReadOnly();
    public Guid? SelectedVenueId { get; private set; }
    public IVenue? SelectedVenue =>
        SelectedVenueId is { } id ? _venues.FirstOrDefault(v => v.Id == id) : null;

    public IOptionsMonitor<ServiceOptions> Options { get; }

    public VenuesService(IOptionsMonitor<ServiceOptions> options, ICacheService cacheService)
    {
        Options = options;
        _cacheService = cacheService;
        Load();
    }

    public async Task<IVenue> CreateAsync(string name)
    {
        var venue = new Venue { Name = name };
        _venues.Add(venue);
        await NotifyAsync();
        return venue;
    }

    public async Task UpdateAsync(Guid venueId, string name, string notes = "", bool enabled = true)
    {
        var venue = _venues.FirstOrDefault(v => v.Id == venueId);
        if (venue is null) return;
        venue.Name = name;
        venue.Notes = notes;
        venue.Enabled = enabled;
        venue.LastUpdated = DateTime.UtcNow;
        await NotifyAsync();
    }

    public async Task RemoveAsync(Guid venueId)
    {
        _venues.RemoveAll(v => v.Id == venueId);
        await NotifyAsync();
    }

    public Task<IPaginatedResult<IVenue>> SearchAsync(string query, int pageNumber = 1, int pageSize = 50)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = _defaultPageSize;
        if (pageSize > _maxPageSize) pageSize = _maxPageSize;

        var filtered = string.IsNullOrWhiteSpace(query)
            ? _venues
            : _venues.Where(v => v.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               v.Notes.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        var totalCount = filtered.Count;
        var items = filtered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var result = new PaginatedResult<IVenue>
        {
            Items = items.Cast<IVenue>().ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        return Task.FromResult<IPaginatedResult<IVenue>>(result);
    }

    public Task<IVenue?> ReadByIdAsync(Guid venueId) =>
        Task.FromResult(_venues.FirstOrDefault(v => v.Id == venueId) as IVenue);

    public Task<IVenue?> ReadByNameAsync(string name) =>
        Task.FromResult(_venues.FirstOrDefault(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) as IVenue);

    public async Task SelectVenueAsync(Guid? venueId)
    {
        SelectedVenueId = venueId;
        StateChanged?.Invoke();
        await Task.CompletedTask;
    }

    private async void Load()
    {
        var venues = await _cacheService.LoadAsync<List<Venue>>(_cacheKey);

        _venues.Clear();

        if (venues?.Count > 0)
        {
            _venues.AddRange(venues);
        }
        else
        {
            _venues.Add(new Venue()
            {
                Name = "Default Venue",
                Notes = "This is the Default Venue"
            });
        }

        StateChanged?.Invoke();
    }

    private async Task SaveAsync()
    {
        await _cacheService.SaveAsync(_cacheKey, _venues);
    }

    private async Task NotifyAsync()
    {
        await SaveAsync();
        StateChanged?.Invoke();
    }

    public class ServiceOptions
    {
        public const string SectionName = nameof(VenuesService);

    }
}
