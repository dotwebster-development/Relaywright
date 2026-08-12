(() => {
    const dialog = document.querySelector('[data-confirm-dialog]');
    if (!dialog || typeof dialog.showModal !== 'function') return;
    const title = dialog.querySelector('[data-confirm-dialog-title]');
    const message = dialog.querySelector('[data-confirm-dialog-message]');
    const confirm = dialog.querySelector('[data-confirm-dialog-confirm]');
    const cancel = dialog.querySelector('[data-confirm-dialog-cancel]');
    let pendingButton = null;

    document.addEventListener('click', (event) => {
        const button = event.target.closest('[data-confirm-button]');
        if (!button || button.dataset.confirmBypass === 'true') return;
        event.preventDefault();
        pendingButton = button;
        title.textContent = button.dataset.confirmTitle ?? 'Confirm action';
        message.textContent = button.dataset.confirmMessage ?? 'Continue with this action?';
        confirm.textContent = button.dataset.confirmAction ?? 'Continue';
        dialog.showModal();
        cancel.focus();
    });
    cancel.addEventListener('click', () => dialog.close());
    confirm.addEventListener('click', () => {
        const button = pendingButton;
        pendingButton = null;
        dialog.close();
        if (!button?.form) return;
        button.dataset.confirmBypass = 'true';
        button.form.requestSubmit(button);
        delete button.dataset.confirmBypass;
    });
    dialog.addEventListener('close', () => {
        pendingButton?.focus();
        pendingButton = null;
    });
})();
