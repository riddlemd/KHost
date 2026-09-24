namespace KHost.Abstractions.Models;

/// <summary>How a page's own list is sorted.</summary>
/// <param name="Column">A repository-defined column name; an unrecognised name is ignored and the
/// list keeps its default order.</param>
/// <param name="Descending">True for largest/last first; false for smallest/first first.</param>
public record SortDescriptor(string Column, bool Descending = false);
