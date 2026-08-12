(() => {
    document.querySelectorAll('[data-toggle-target]').forEach((toggle) => {
        const target = document.querySelector(toggle.dataset.toggleTarget);
        if (!target) return;
        const sync = () => {
            target.hidden = !toggle.checked;
            target.querySelectorAll('input, select, textarea').forEach((field) => {
                field.disabled = !toggle.checked;
            });
        };
        toggle.addEventListener('change', sync);
        sync();
    });
})();
