using System;
using System.Collections.Generic;
using System.Text;

namespace KHost.Abstractions.Models
{
    /// <summary>Lyrics found for a song, to show a host who asked for the words.</summary>
    public class Lyrics
    {
        /// <summary>The song's title and artist, as the provider matched it.</summary>
        public required string Name { get; set; }

        /// <summary>The lyrics themselves.</summary>
        public required string Text { get; set; }

        /// <summary>The provider's display name, for attribution.</summary>
        public required string ProviderName { get; set; }

        /// <summary>A link to the provider's own page for this song, for attribution.</summary>
        public required string ProviderUrl { get; set; }
    }
}
