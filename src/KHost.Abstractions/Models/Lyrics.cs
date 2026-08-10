using System;
using System.Collections.Generic;
using System.Text;

namespace KHost.Abstractions.Models
{
    public class Lyrics
    {
        public required string Name { get; set; }
        public required string Text { get; set; }
        public required string ProviderName { get; set; }
        public required string ProviderUrl { get; set; }
    }
}
