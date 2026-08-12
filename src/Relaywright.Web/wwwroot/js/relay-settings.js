(() => {
    const authToggle = document.querySelector('[data-auth-toggle]');
    const authPanel = document.querySelector('[data-auth-panel]');
    const authMode = document.querySelector('[data-auth-mode]');
    const authType = document.querySelector('[data-auth-type]');
    const authIdentityLabel = document.querySelector('[data-auth-identity-label]');
    const bindAllToggle = document.querySelector('[data-bind-all]');
    const bindSelect = document.querySelector('[data-bind-select]');
    const startTlsToggle = document.querySelector('[data-starttls-toggle]');
    const certificatePanel = document.querySelector('[data-certificate-panel]');
    const basicFields = document.querySelectorAll('[data-auth-basic]');
    const microsoftFields = document.querySelectorAll('[data-auth-microsoft]');
    const authIdentity = document.querySelector('[data-auth-identity]');

    const syncAuth = () => {
        const enabled = !!authToggle?.checked;
        const isMicrosoft = authMode?.value === 'Microsoft365OAuth';
        if (authPanel) authPanel.hidden = !enabled;
        if (authIdentityLabel) authIdentityLabel.textContent = isMicrosoft ? 'Mailbox' : 'User Name';
        if (authType) authType.hidden = !enabled;
        if (authIdentity) authIdentity.hidden = !enabled || !authMode?.value;
        basicFields.forEach((element) => element.hidden = !enabled || isMicrosoft || !authMode?.value);
        microsoftFields.forEach((element) => element.hidden = !enabled || !isMicrosoft || !authMode?.value);
    };
    const syncBindSelection = () => bindSelect?.classList.toggle('is-collapsed', !!bindAllToggle?.checked);
    const syncCertificate = () => {
        if (certificatePanel) certificatePanel.hidden = !startTlsToggle?.checked;
    };
    authToggle?.addEventListener('change', syncAuth);
    authMode?.addEventListener('change', syncAuth);
    bindAllToggle?.addEventListener('change', syncBindSelection);
    startTlsToggle?.addEventListener('change', syncCertificate);
    syncAuth();
    syncBindSelection();
    syncCertificate();
})();
