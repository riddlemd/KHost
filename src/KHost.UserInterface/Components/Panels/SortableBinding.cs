using Microsoft.JSInterop;

namespace KHost.UserInterface.Components.Panels;

/// <summary>Wires one draggable list to SortableJS. Both queue panels attach and detach the same
/// way as their permission and row count come and go, so the dance lives here once rather than
/// twice.</summary>
/// <remarks>A plain class, not a base class: each panel already has its own render lifecycle, and
/// this only needs to be told when the target state changes.</remarks>
public sealed class SortableBinding(
    IJSRuntime js, string key, string selector, string filter, string itemIdAttribute, string callbackName)
{
    private DotNetObjectReference<object>? _dotNetRef;
    private bool _attached;

    /// <summary>Attaches or detaches to match <paramref name="enabled"/>, doing nothing when
    /// already in that state. <paramref name="target"/> is the component whose
    /// <c>[JSInvokable]</c> method (<see cref="callbackName"/>) SortableJS calls back into.</summary>
    public async Task SyncAsync(bool enabled, object target)
    {
        if (enabled == _attached) return;

        if (enabled)
        {
            _dotNetRef ??= DotNetObjectReference.Create(target);

            // The row itself drags, so no handle selector.
            await js.InvokeVoidAsync(
                "khSortable.init", key, selector, filter, _dotNetRef, callbackName, itemIdAttribute);
        }
        else
        {
            await js.InvokeVoidAsync("khSortable.destroy", key);
        }

        _attached = enabled;
    }

    /// <summary>Tearing the sortable down is a JS call, and the circuit is usually gone by the
    /// time a component disposes.</summary>
    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();

        try
        {
            await js.InvokeVoidAsync("khSortable.destroy", key);
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
