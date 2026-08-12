(() => {
    document.querySelectorAll('[data-certificate-modes]').forEach((root) => {
        const modeInputs = Array.from(root.querySelectorAll('[data-certificate-mode]'));
        const scope = root.closest('form') ?? document;
        const panels = Array.from(scope.querySelectorAll('[data-certificate-panel]'));
        if (!modeInputs.length || !panels.length) return;

        const syncPanels = () => {
            const selected = modeInputs.find((input) => input.checked)?.dataset.certificateMode ?? 'selfsigned';
            panels.forEach((panel) => {
                const active = panel.dataset.certificatePanel === selected;
                panel.hidden = !active;
                panel.querySelectorAll('input, select, textarea').forEach((field) => {
                    field.disabled = !active;
                });
            });
        };
        modeInputs.forEach((input) => input.addEventListener('change', syncPanels));
        syncPanels();
    });
})();
