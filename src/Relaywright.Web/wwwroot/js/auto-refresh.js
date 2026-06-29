(() => {
    const controls = document.querySelectorAll('[data-auto-refresh]');

    controls.forEach((control) => {
        const key = control.dataset.autoRefresh || window.location.pathname;
        const toggle = control.querySelector('[data-auto-refresh-toggle]');
        const interval = control.querySelector('[data-auto-refresh-interval]');
        const status = control.querySelector('[data-auto-refresh-status]');
        let timer = null;

        if (!toggle || !interval) {
            return;
        }

        const enabledKey = `${key}:enabled`;
        const intervalKey = `${key}:interval`;
        const savedEnabled = window.localStorage.getItem(enabledKey);
        const savedInterval = window.localStorage.getItem(intervalKey);

        toggle.checked = savedEnabled === 'true';
        if (savedInterval && Array.from(interval.options).some((option) => option.value === savedInterval)) {
            interval.value = savedInterval;
        }

        const hasPendingSelection = () =>
            document.querySelector('form[method="post"] input[type="checkbox"]:checked') !== null;

        const schedule = () => {
            window.clearTimeout(timer);

            if (!toggle.checked) {
                if (status) {
                    status.textContent = 'Off';
                }

                return;
            }

            const seconds = Number.parseInt(interval.value, 10);
            if (!Number.isFinite(seconds) || seconds <= 0) {
                return;
            }

            if (status) {
                status.textContent = `${seconds}s`;
            }

            timer = window.setTimeout(() => {
                if (document.hidden || hasPendingSelection()) {
                    schedule();
                    return;
                }

                window.location.reload();
            }, seconds * 1000);
        };

        toggle.addEventListener('change', () => {
            window.localStorage.setItem(enabledKey, toggle.checked ? 'true' : 'false');
            schedule();
        });

        interval.addEventListener('change', () => {
            window.localStorage.setItem(intervalKey, interval.value);
            schedule();
        });

        document.addEventListener('visibilitychange', schedule);
        schedule();
    });
})();
