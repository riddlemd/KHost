using System;
using System.Collections.Generic;
using System.Text;

namespace KHost.Abstractions.Models
{
    public class MediaProviderAction
    {
        public required string DisplayName { get; set; }
        public required Func<string, Task> PerformAsync { get; set; }
    }
}
