const handlers = new WeakMap();

export function initialize(dialog, callback) {
    let pointerStartedOutside = false;
    const dismiss = () => callback.invokeMethodAsync("DismissDetailsAsync");
    const cancel = event => {
        event.preventDefault();
        dismiss();
    };
    const keyDown = event => {
        if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            dismiss();
        }
    };
    const pointerDown = event => {
        pointerStartedOutside = event.target === dialog && isOutside(dialog, event);
    };
    const click = event => {
        if (pointerStartedOutside && event.target === dialog && isOutside(dialog, event)) {
            dismiss();
        }
        pointerStartedOutside = false;
    };
    dialog.addEventListener("cancel", cancel);
    dialog.addEventListener("keydown", keyDown);
    dialog.addEventListener("pointerdown", pointerDown);
    dialog.addEventListener("click", click);
    handlers.set(dialog, { cancel, keyDown, pointerDown, click });
}

function isOutside(dialog, event) {
    const bounds = dialog.getBoundingClientRect();
    return event.clientX < bounds.left || event.clientX > bounds.right
        || event.clientY < bounds.top || event.clientY > bounds.bottom;
}

export function setOpen(dialog, open) {
    if (open && !dialog.open) {
        dialog.showModal();
    } else if (!open && dialog.open) {
        dialog.close();
    }
}

export function dispose(dialog) {
    const registered = handlers.get(dialog);
    if (registered) {
        dialog.removeEventListener("cancel", registered.cancel);
        dialog.removeEventListener("keydown", registered.keyDown);
        dialog.removeEventListener("pointerdown", registered.pointerDown);
        dialog.removeEventListener("click", registered.click);
        handlers.delete(dialog);
    }
    if (dialog.open) {
        dialog.close();
    }
}