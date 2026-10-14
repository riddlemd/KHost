using KHost.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using KHost.Common.Orthography;
using Unidecode.NET;

namespace KHost.DataAccess.Contexts;

/// <summary>Keeps every folded column in step with the text it mirrors.</summary>
/// <remarks>Applied on save rather than in the model, so nothing writing through EF forgets.</remarks>
internal static class EntityFolding
{
    // Named rather than relying on Unidecoder.Algorithm, which is process-wide mutable state
    // anything in the process could flip, changing what every stored folded value means.
    private const UnidecodeAlgorithm Algorithm = UnidecodeAlgorithm.Complete;

    // Named, not nameof: most are shadow properties, kept off the models a plugin sees.
    internal const string SearchFolded = "SearchFolded";
    internal const string NameFolded = "NameFolded";
    internal const string NotesFolded = "NotesFolded";
    internal const string TitleFolded = "TitleFolded";
    internal const string ArtistFolded = "ArtistFolded";

    /// <summary>Transliterates to ASCII and lowercases: "Björk" to "bjork".</summary>
    /// <remarks>Accents and non-Latin scripts both reduce to something a host can type.</remarks>
    internal static string Fold(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Unidecode(Algorithm).ToLowerInvariant().Trim();

    /// <summary><see cref="Fold"/> with stylised spellings resolved first; media only.</summary>
    /// <remarks>A roster singer called "P!nk" is a different person from "Pink".</remarks>
    internal static string FoldMedia(string? value) => Fold(StylisedSpelling.ResolveToPlainSpelling(value));

    internal static void Apply(ChangeTracker changeTracker)
    {
        foreach (var entry in changeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;

            switch (entry.Entity)
            {
                case Media media:
                    Set(entry, SearchFolded, FoldMedia($"{media.Title} {media.Artist}"));
                    Set(entry, TitleFolded, FoldMedia(media.Title));
                    Set(entry, ArtistFolded, FoldMedia(media.Artist));
                    break;

                case KHostUser user:
                    Set(entry, NameFolded, Fold(user.Name));
                    break;

                case Venue venue:
                    Set(entry, NameFolded, Fold(venue.Name));
                    break;

                case KHostUserGroup group:
                    Set(entry, NameFolded, Fold(group.Name));
                    break;

                case Tip tip:
                    Set(entry, NotesFolded, Fold(tip.Notes));
                    break;

                case MediaPool pool:
                    Set(entry, NameFolded, Fold(pool.Name));
                    break;

                case VisualisationPlaylist playlist:
                    Set(entry, NameFolded, Fold(playlist.Name));
                    break;
            }
        }
    }

    // Marked modified explicitly: a partial update writes only the properties it was told to. Null is
    // not the empty fold: a shadow column attached from a detached entity starts null and must be written.
    private static void Set(EntityEntry entry, string property, string folded)
    {
        var column = entry.Property(property);

        if (column.CurrentValue is string current && current == folded)
            return;

        column.CurrentValue = folded;
        column.IsModified = true;
    }
}
