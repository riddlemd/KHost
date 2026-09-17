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
                    Set(entry, nameof(Media.SearchFolded), media.SearchFolded,
                        FoldMedia($"{media.Title} {media.Artist}"));
                    break;

                case KHostUser user:
                    Set(entry, nameof(KHostUser.NameFolded), user.NameFolded, Fold(user.Name));
                    break;

                case Venue venue:
                    Set(entry, nameof(Venue.NameFolded), venue.NameFolded, Fold(venue.Name));
                    break;

                case KHostUserGroup group:
                    Set(entry, nameof(KHostUserGroup.NameFolded), group.NameFolded, Fold(group.Name));
                    break;

                case Tip tip:
                    Set(entry, nameof(Tip.NotesFolded), tip.NotesFolded, Fold(tip.Notes));
                    break;

                case MediaPool pool:
                    Set(entry, nameof(MediaPool.NameFolded), pool.NameFolded, Fold(pool.Name));
                    break;
            }
        }
    }

    // Marking the property modified explicitly: an entry attached for a partial update writes only
    // the properties it was told to, and the folded column has to travel with the text it mirrors.
    private static void Set(EntityEntry entry, string property, string current, string folded)
    {
        if (current == folded)
            return;

        entry.Property(property).CurrentValue = folded;
        entry.Property(property).IsModified = true;
    }
}
