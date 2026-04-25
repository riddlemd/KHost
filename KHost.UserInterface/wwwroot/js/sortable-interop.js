window.singerQueueSortable = {
    instance: null,

    init(containerSelector, dotNetRef) {
        const el = document.querySelector(containerSelector);
        if (!el) return;

        if (this.instance) this.instance.destroy();

        this.instance = Sortable.create(el, {
            animation: 150,
            handle: '.kh-singer-queue-panel__drag-handle',
            filter: '.kh-singer-queue-panel__singer-queue__singer--locked',
            onEnd(evt) {
                const singerId = evt.item.dataset.singerId;
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

                dotNetRef.invokeMethodAsync('OnSortEnd', singerId, evt.newIndex);
            }
        });
    },

    destroy() {
        this.instance?.destroy();
        this.instance = null;
    }
};
