# Relaywright Wiki Drafts

These files are the reviewable source drafts for the GitHub Wiki.

The repository documentation remains the source of truth for release-sensitive behavior, validation evidence, architecture, and security policy. The Wiki is the practical operator manual, so these pages are task-focused and version-aware for the current stable `1.0.x` release line.

The GitHub Wiki repository is initialized. To publish these pages, copy or push the Markdown files in this folder to the Wiki repository:

```powershell
git clone https://github.com/dotwebster-development/Relaywright.wiki.git Relaywright.wiki
Get-ChildItem docs\wiki-drafts\*.md |
  Where-Object { $_.Name -ne "README.md" } |
  Copy-Item -Destination Relaywright.wiki\
Push-Location Relaywright.wiki
git status --short
git add *.md
git commit -m "Update operator wiki"
git push origin master
Pop-Location
```

If the clone reports `Repository not found` in a new fork or recreated repository, create the first page from the GitHub Wiki UI, then rerun the clone/copy/push flow. GitHub does not expose the backing `.wiki.git` repository until the Wiki has been initialized.

Do not publish secrets, production hostnames, raw message bodies, SMTP transcripts, tokens, protected blobs, private infrastructure details, or customer data in Wiki pages.
