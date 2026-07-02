using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Relaywright.Web.Infrastructure;
using Relaywright.Web.Options;

namespace Relaywright.Web.Services.Updates;

public sealed class UpdateCheckService(
    IOptions<UpdateCheckOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<UpdateCheckService> logger) : IUpdateCheckService
{
    public const string HttpClientName = "Relaywright.UpdateCheck";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _statusGate = new();
    private UpdateCheckStatus _status = CreateInitialStatus(options.Value);

    public Task<UpdateCheckStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var currentOptions = options.Value;
        if (!currentOptions.Enabled)
        {
            var disabled = UpdateCheckStatus.Disabled(ApplicationVersion.DisplayVersion, NormalizeRepository(currentOptions.Repository));
            SetStatus(disabled);
            return Task.FromResult(disabled);
        }

        lock (_statusGate)
        {
            return Task.FromResult(_status);
        }
    }

    public async Task<UpdateCheckStatus> RefreshAsync(CancellationToken cancellationToken)
    {
        var currentOptions = options.Value;
        var repository = NormalizeRepository(currentOptions.Repository);
        var currentVersion = ApplicationVersion.DisplayVersion;

        if (!currentOptions.Enabled)
        {
            return SetStatus(UpdateCheckStatus.Disabled(currentVersion, repository));
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow;
            SetStatus(new UpdateCheckStatus(
                UpdateCheckState.Checking,
                currentVersion,
                repository,
                LastCheckedUtc: now,
                Message: "Checking GitHub Releases."));

            if (!TryCreateReleaseUri(repository, out var releaseUri))
            {
                return SetStatus(CreateFailureStatus(
                    UpdateCheckState.CheckFailed,
                    currentVersion,
                    repository,
                    "Update check repository must be in owner/name form.",
                    currentOptions,
                    now));
            }

            try
            {
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(currentOptions.GetTimeout());

                using var request = new HttpRequestMessage(HttpMethod.Get, releaseUri);
                request.Headers.TryAddWithoutValidation("User-Agent", $"Relaywright/{currentVersion}");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

                var client = httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutSource.Token);

                if (!response.IsSuccessStatusCode)
                {
                    return SetStatus(CreateFailureStatus(
                        UpdateCheckState.CheckFailed,
                        currentVersion,
                        repository,
                        $"GitHub release check failed with HTTP {(int)response.StatusCode}.",
                        currentOptions,
                        now));
                }

                await using var stream = await response.Content.ReadAsStreamAsync(timeoutSource.Token);
                var release = await JsonSerializer.DeserializeAsync<GitHubReleaseInfo>(
                    stream,
                    JsonOptions,
                    timeoutSource.Token);

                return SetStatus(CreateReleaseStatus(currentVersion, repository, release, currentOptions, now));
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Relaywright release update check failed. Repository={Repository}", repository);
                return SetStatus(CreateFailureStatus(
                    UpdateCheckState.CheckFailed,
                    currentVersion,
                    repository,
                    "GitHub release check failed. Review connectivity and try again.",
                    currentOptions,
                    now));
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private static UpdateCheckStatus CreateInitialStatus(UpdateCheckOptions options)
    {
        var repository = NormalizeRepository(options.Repository);
        return options.Enabled
            ? UpdateCheckStatus.NeverChecked(ApplicationVersion.DisplayVersion, repository)
            : UpdateCheckStatus.Disabled(ApplicationVersion.DisplayVersion, repository);
    }

    private static UpdateCheckStatus CreateReleaseStatus(
        string currentVersion,
        string repository,
        GitHubReleaseInfo? release,
        UpdateCheckOptions options,
        DateTimeOffset checkedUtc)
    {
        if (release is null || release.Draft || release.Prerelease || string.IsNullOrWhiteSpace(release.TagName))
        {
            return CreateFailureStatus(
                UpdateCheckState.InvalidRelease,
                currentVersion,
                repository,
                "Latest GitHub release metadata was incomplete or not stable.",
                options,
                checkedUtc);
        }

        if (!SemanticVersionInfo.TryParse(currentVersion, out var current))
        {
            return CreateFailureStatus(
                UpdateCheckState.InvalidRelease,
                currentVersion,
                repository,
                $"Current version '{currentVersion}' could not be parsed.",
                options,
                checkedUtc);
        }

        if (!SemanticVersionInfo.TryParse(release.TagName, out var latest))
        {
            return CreateFailureStatus(
                UpdateCheckState.InvalidRelease,
                currentVersion,
                repository,
                $"Latest release tag '{release.TagName}' could not be parsed.",
                options,
                checkedUtc);
        }

        var comparison = latest!.CompareTo(current!);
        var latestVersion = latest.ToString();
        var state = comparison switch
        {
            > 0 => UpdateCheckState.UpdateAvailable,
            0 => UpdateCheckState.UpToDate,
            _ => UpdateCheckState.CurrentAhead
        };
        var message = state switch
        {
            UpdateCheckState.UpdateAvailable => $"Relaywright {latestVersion} is available.",
            UpdateCheckState.UpToDate => $"Relaywright {currentVersion} is current.",
            UpdateCheckState.CurrentAhead => $"Current version {currentVersion} is newer than the latest stable release.",
            _ => "Release check completed."
        };

        return new UpdateCheckStatus(
            state,
            currentVersion,
            repository,
            LatestVersion: latestVersion,
            ReleaseName: release.Name,
            ReleaseUrl: release.HtmlUrl,
            ReleasePublishedUtc: release.PublishedAt,
            LastCheckedUtc: checkedUtc,
            NextCheckUtc: checkedUtc.Add(options.GetInterval()),
            Message: message);
    }

    private static UpdateCheckStatus CreateFailureStatus(
        UpdateCheckState state,
        string currentVersion,
        string repository,
        string message,
        UpdateCheckOptions options,
        DateTimeOffset checkedUtc)
    {
        return new UpdateCheckStatus(
            state,
            currentVersion,
            repository,
            LastCheckedUtc: checkedUtc,
            NextCheckUtc: checkedUtc.Add(options.GetInterval()),
            Message: message);
    }

    private static bool TryCreateReleaseUri(string repository, out Uri releaseUri)
    {
        releaseUri = new Uri("https://api.github.com/");
        var parts = repository.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        releaseUri = new Uri($"https://api.github.com/repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}/releases/latest");
        return true;
    }

    private static string NormalizeRepository(string? repository)
    {
        return string.IsNullOrWhiteSpace(repository)
            ? UpdateCheckOptions.DefaultRepository
            : repository.Trim();
    }

    private UpdateCheckStatus SetStatus(UpdateCheckStatus status)
    {
        lock (_statusGate)
        {
            _status = status;
            return _status;
        }
    }
}
