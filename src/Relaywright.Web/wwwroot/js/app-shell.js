(() => {
    const root = document.documentElement;
    const toggle = document.querySelector('[data-nav-toggle]');
    const dismiss = document.querySelector('[data-nav-dismiss]');
    const rail = document.getElementById('app-rail');

    if (toggle && dismiss && rail) {
        let returnFocus = null;
        const focusableSelector = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';
        const setOpen = (isOpen) => {
            root.classList.toggle('nav-open', isOpen);
            toggle.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
            if (isOpen) {
                returnFocus = document.activeElement;
                rail.querySelector(focusableSelector)?.focus();
            } else if (returnFocus) {
                returnFocus.focus();
                returnFocus = null;
            }
        };
        const close = () => setOpen(false);

        toggle.addEventListener('click', () => setOpen(!root.classList.contains('nav-open')));
        dismiss.addEventListener('click', close);
        rail.querySelectorAll('a').forEach((link) => link.addEventListener('click', () => {
            if (window.innerWidth <= 960) close();
        }));
        rail.addEventListener('keydown', (event) => {
            if (event.key !== 'Tab' || window.innerWidth > 960 || !root.classList.contains('nav-open')) return;
            const focusable = Array.from(rail.querySelectorAll(focusableSelector));
            if (focusable.length === 0) return;
            const first = focusable[0];
            const last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault();
                first.focus();
            }
        });
        window.addEventListener('keydown', (event) => {
            if (event.key === 'Escape' && root.classList.contains('nav-open')) close();
        });
        const desktopMedia = window.matchMedia('(min-width: 961px)');
        const handleDesktopChange = (event) => {
            if (event.matches) close();
        };
        desktopMedia.addEventListener?.('change', handleDesktopChange);
        if (!desktopMedia.addEventListener) desktopMedia.addListener(handleDesktopChange);
    }

    document.querySelectorAll('[data-dismissible-status]').forEach((message) => {
        message.querySelector('[data-status-dismiss]')?.addEventListener('click', () => message.remove());
    });
})();

(() => {
    const root = document.querySelector('[data-settings-search]');
    if (!root) return;

    const input = root.querySelector('[data-settings-search-input]');
    const results = root.querySelector('[data-settings-search-results]');
    const items = Array.from(root.querySelectorAll('[data-settings-search-item]'));
    const empty = root.querySelector('[data-settings-search-empty]');
    const announcement = root.querySelector('[data-settings-search-announcement]');
    if (!input || !results || items.length === 0) return;

    let activeIndex = -1;
    const normalize = (value) => value.trim().toLowerCase();
    const visibleItems = () => items.filter((item) => !item.hidden);
    const setOpen = (isOpen) => {
        results.hidden = !isOpen;
        input.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
        if (!isOpen) {
            activeIndex = -1;
            input.removeAttribute('aria-activedescendant');
        }
    };
    const setActive = (index) => {
        const visible = visibleItems();
        visible.forEach((item) => item.classList.remove('is-active'));
        if (visible.length === 0) {
            activeIndex = -1;
            input.removeAttribute('aria-activedescendant');
            return;
        }
        activeIndex = (index + visible.length) % visible.length;
        const active = visible[activeIndex];
        active.classList.add('is-active');
        input.setAttribute('aria-activedescendant', active.id);
        active.scrollIntoView({ block: 'nearest' });
    };
    const update = () => {
        const query = normalize(input.value);
        let matchCount = 0;
        items.forEach((item) => {
            const haystack = normalize(item.dataset.settingsSearchText ?? item.textContent ?? '');
            const isMatch = query.length > 0 && haystack.includes(query);
            item.hidden = !isMatch;
            item.classList.remove('is-active');
            if (isMatch) matchCount++;
        });
        if (empty) empty.hidden = query.length === 0 || matchCount > 0;
        if (announcement) announcement.textContent = query.length === 0 ? '' : `${matchCount} matching settings`;
        activeIndex = -1;
        input.removeAttribute('aria-activedescendant');
        setOpen(query.length > 0);
    };

    input.addEventListener('input', update);
    input.addEventListener('focus', update);
    input.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') {
            input.value = '';
            update();
            input.blur();
        } else if (event.key === 'ArrowDown') {
            event.preventDefault();
            setActive(activeIndex + 1);
        } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            setActive(activeIndex - 1);
        } else if (event.key === 'Enter') {
            const visible = visibleItems();
            const selected = visible[activeIndex] ?? visible[0];
            if (selected) {
                event.preventDefault();
                selected.click();
            }
        }
    });
    document.addEventListener('click', (event) => {
        if (!root.contains(event.target)) setOpen(false);
    });
})();
