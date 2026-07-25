## Summary

Describe the operator problem and the focused change that addresses it.

## Validation

- [ ] `dotnet build Relaywright.sln`
- [ ] `dotnet test tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj`
- [ ] Additional affected workflow, installer, website, or release checks are listed below.

Validation evidence:

## Safety And Operations

- [ ] Accepted SMTP DATA remains durable before `250 OK`.
- [ ] Trusted-network enforcement is preserved.
- [ ] Queue state and spool cleanup ordering are preserved.
- [ ] No credentials, tokens, message bodies, protected blobs, certificates, keys, or private infrastructure details were added to source, logs, fixtures, or screenshots.
- [ ] Listener-setting changes still notify the runtime configuration path.
- [ ] Queue-affecting changes wake the delivery worker when required.
- [ ] Not applicable items are explained below.

Notes:

## Documentation Impact

- [ ] Repository documentation is updated or no update is required.
- [ ] Operator-facing Wiki drafts are updated or no update is required.
- [ ] Public website and screenshots are updated or no update is required.
- [ ] Release notes or release validation evidence are updated or no update is required.

Documentation notes:
