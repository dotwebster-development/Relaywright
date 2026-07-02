# Branch Workflow

Relaywright keeps `main` and `development` protected. Normal work must land through a pull request so GitHub Actions can run the required checks before the protected branch changes.

## Preferred Daily Flow

Start new work from the latest `development` branch:

```powershell
git switch development
git pull --ff-only origin development
git switch -c <short-topic-branch>
```

Make the change, run the relevant checks, commit, then push the topic branch:

```powershell
dotnet build Relaywright.sln
dotnet test tests/Relaywright.Web.Tests/Relaywright.Web.Tests.csproj

git status --short
git add <changed-files>
git commit -m "<clear commit message>"
git push -u origin <short-topic-branch>
```

Open a pull request from `<short-topic-branch>` into `development`. Wait for the required `build-test` check to pass, then merge the pull request on GitHub.

## If Direct Push To Development Is Rejected

This is expected when branch protection is enabled. The error usually looks like:

```text
Protected branch update failed for refs/heads/development.
Changes must be made through a pull request.
Required status check "build-test" is expected.
```

If the commit was already made locally on `development`, publish that exact commit to a topic branch instead:

```powershell
git status --short --branch
git push origin HEAD:refs/heads/<short-topic-branch>
```

Then open the pull request shown by GitHub, targeting `development`.

## After The Pull Request Is Merged

Sync local `development` before starting more work:

```powershell
git switch development
git fetch origin
git pull --ff-only origin development
```

If GitHub used squash merge, local `development` may show as both ahead and behind because the local commit and the squash commit have different IDs. Only after confirming there is no uncommitted work and the pull request was merged, realign local `development` to the remote protected branch:

```powershell
git status --short --branch
git reset --hard origin/development
```

Do not run the reset command when there is uncommitted work.

## When Asking Codex To Handle It

Ask Codex to:

- check `git status --short --branch` and `git remote -v`;
- confirm the diff only contains the intended work;
- commit with a focused message;
- try the requested protected-branch push only if explicitly asked;
- if GitHub rejects the push, push the commit to a short topic branch;
- report the pull request URL and the local branch state;
- after the pull request is merged, sync local `development` and only reset it when the merged PR and clean worktree make that safe.

For small cleanup branches, use names like `cleanup/unused-test-support`. For feature work, use names like `feature/<short-description>`. For fixes, use names like `fix/<short-description>`.
