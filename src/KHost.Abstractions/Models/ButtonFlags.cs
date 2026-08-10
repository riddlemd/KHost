using System;
using System.Collections.Generic;
using System.Text;

namespace KHost.Abstractions.Models
{

    [Flags]
    public enum ButtonFlags : byte
    {
        ShowText = 0b00000001,
        ShowIcon = 0b00000010,

        Default = ShowText | ShowIcon
    }
}
