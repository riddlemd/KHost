window.khSortable = {
    instances: {},

    init(key, containerSelector, handleSelector, filterSelector, dotNetRef, sortEndMethod, itemIdAttribute) {
        const el = document.querySelector(containerSelector);
        if (!el) return;

        this.destroy(key);

        this.instances[key] = Sortable.create(el, {
            animation: 150,
            handle: handleSelector || undefined,
            filter: filterSelector || undefined,
            preventOnFilter: false,
            onEnd(evt) {
                const itemId = evt.item.dataset[itemIdAttribute];
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
