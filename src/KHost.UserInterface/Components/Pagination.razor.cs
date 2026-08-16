using Microsoft.AspNetCore.Components;

namespace KHost.UserInterface.Components;

public partial class Pagination
{
    [Parameter] public int CurrentPage { get; set; }
    [Parameter] public int TotalPages { get; set; }
    [Parameter] public EventCallback OnPreviousPage { get; set; }
    [Parameter] public EventCallback OnNextPage { get; set; }
}
