# KHost.LrcLib

A typed .NET HTTP client for the [LRCLIB](https://lrclib.net) lyrics API. Provides `ILrcLibClient` with DI registration and strongly-typed request/response models.

## Registration

Call `AddLrcLib()` on your `IServiceCollection`. It is already called by `KHost.Domain`'s `AddDomain()` — you only need this directly if consuming the client outside of that context.

```csharp
services.AddLrcLib();
```

This registers:
- `ILrcLibClient` → `LrcLibClient` (typed `HttpClient`)
- `LrcLibOptions` bound from the `LrcLibClient` configuration section

### Configuration

```json
{
  "LrcLibClient": {
    "BaseAddress": "https://lrclib.net/",
    "UserAgent": "MyApp/1.0 (+https://example.com)"
  }
}
```

Both fields have defaults and are optional.

## API

### `ILrcLibClient`

#### `GetAsync(GetLyricsRequest, CancellationToken)`

Fetches a single lyrics record by track name, artist, and optional album/duration. Maps to `GET /api/get`.

```csharp
var record = await client.GetAsync(new GetLyricsRequest("Mr. Brightside", "The Killers"));
```

Both `TrackName` and `ArtistName` are required — throws `ArgumentException` if either is blank.

#### `GetCachedAsync(GetLyricsRequest, CancellationToken)`

Same as `GetAsync` but hits `GET /api/get-cached`, which returns a cached result without triggering an upstream fetch on cache miss.

#### `GetByIdAsync(long, CancellationToken)`

Fetches a record by its LRCLIB numeric ID. Maps to `GET /api/get/{id}`.

```csharp
var record = await client.GetByIdAsync(3396226);
```

#### `SearchAsync(SearchLyricsRequest, CancellationToken)`

Full-text search across the LRCLIB catalogue. Maps to `GET /api/search`. Either `Query` or `TrackName` must be provided.

```csharp
var results = await client.SearchAsync(new SearchLyricsRequest(Query: "brightside killers"));
// or
var results = await client.SearchAsync(new SearchLyricsRequest(TrackName: "Mr. Brightside", ArtistName: "The Killers"));
```

Returns `IReadOnlyList<LyricsRecord>`.

## Models

### `GetLyricsRequest`

| Property | Type | Required | Description |
|---|---|---|---|
| `TrackName` | `string` | Yes | Track title |
| `ArtistName` | `string` | Yes | Artist name |
| `AlbumName` | `string?` | No | Album name (improves match accuracy) |
| `Duration` | `double?` | No | Track duration in seconds |

### `SearchLyricsRequest`

| Property | Type | Description |
|---|---|---|
| `Query` | `string?` | Free-text query (`q` parameter) |
| `TrackName` | `string?` | Track title filter |
| `ArtistName` | `string?` | Artist name filter |
| `AlbumName` | `string?` | Album name filter |

### `LyricsRecord`

| Property | Type | Description |
|---|---|---|
| `Id` | `long` | LRCLIB record ID |
| `TrackName` | `string` | Track title |
| `ArtistName` | `string` | Artist name |
| `AlbumName` | `string?` | Album name |
| `Duration` | `double?` | Duration in seconds |
| `Instrumental` | `bool` | Whether the track is instrumental |
| `PlainLyrics` | `string?` | Unsynced lyrics text |
| `SyncedLyrics` | `string?` | LRC-format synced lyrics |

`SyncedLyrics` takes precedence over `PlainLyrics` where available.

## Notes

- Returns `null` on 404 (no match found) rather than throwing.
- Non-404 HTTP errors surface as `HttpRequestException`.
- The `UserAgent` header is required by the LRCLIB API's fair-use policy — keep it set to something that identifies your application.
