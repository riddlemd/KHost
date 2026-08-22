// Keyed by list: the console has two sortable queues on screen at once — the singers and the
// selected singer's songs — and a single shared instance would have each init tear the other down.
window.khSortable = {
    instances: {},

    init(key, containerSelector, handleSelector, filterSelector, dotNetRef, sortEndMethod, itemIdAttribute) {
        const el = document.querySelector(containerSelector);
        if (!el) return;

        this.destroy(key);

        this.instances[key] = Sortable.create(el, {
            animation: 150,
            // No handle means the whole row drags. Whatever is excluded then has to come through
            // `filter`, and preventOnFilter must be off or Sortable swallows those elements'
            // clicks along with the drag — the row's buttons would stop working.
            handle: handleSelector || undefined,
            filter: filterSelector || undefined,
            preventOnFilter: false,
            onEnd(evt) {
                const itemId = evt.item.dataset[itemIdAttribute];
                // Revert the DOM to its pre-drag order. Blazor Server maintains a
                // server-side virtual DOM and doesn't know SortableJS moved nodes.
                // If we leave the DOM in SortableJS's order, Blazor's diff will be
                // applied against the wrong baseline and produce an incorrect result.
                if (evt.newIndex !== evt.oldIndex) {
                    if (evt.newIndex < evt.oldIndex) {
                        evt.from.insertBefore(evt.item, evt.from.children[evt.oldIndex + 1] || null);
                    } else {
                        evt.from.insertBefore(evt.item, evt.from.children[evt.oldIndex]);
                    }
                }

                dotNetRef.invokeMethodAsync(sortEndMethod, itemId, evt.newIndex);
            }
        });
    },

    destroy(key) {
        this.instances[key]?.destroy();
        delete this.instances[key];
    }
};
