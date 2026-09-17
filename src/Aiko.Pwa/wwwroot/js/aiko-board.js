// The board's drag handle.
//
// Flare's drag context starts a drag from anywhere inside a draggable item: its filter only asks
// whether the press landed inside `[data-flare-drag]`. On a board that means a click on a card can
// never open it - the drag gesture captures the pointer, so the click never reaches the card.
//
// Flare documents one switch for this: an item carrying `data-flare-drag-disabled="true"` is skipped by
// that filter. The attribute is read at pointerdown, so setting it from a capture-phase listener that
// runs before Flare's own listener decides the gesture by where the press started: the handle drags,
// the rest of the card opens.
//
// Registered once for the whole document rather than per board, the same way aikoHotkey is: the board's
// cards are re-rendered as the projection changes, and a listener per render would leak.
window.aikoDragHandle = {
    handleSelector: '.aiko-card__handle',
    itemSelector: '[data-flare-drag]',
    enable: function () {
        if (window.aikoDragHandleBound) {
            return;
        }
        window.aikoDragHandleBound = true;

        const itemOf = function (event) {
            const target = event.target;
            if (!target || !target.closest) {
                return null;
            }
            return target.closest(window.aikoDragHandle.itemSelector);
        };

        document.addEventListener('pointerdown', function (event) {
            const item = itemOf(event);
            if (!item) {
                return;
            }
            const onHandle = event.target.closest && event.target.closest(window.aikoDragHandle.handleSelector);
            item.dataset.flareDragDisabled = onHandle ? 'false' : 'true';
        }, true);

        const release = function (event) {
            const item = itemOf(event);
            if (item) {
                delete item.dataset.flareDragDisabled;
            }
        };
        document.addEventListener('pointerup', release, true);
        document.addEventListener('pointercancel', release, true);
    }
};
