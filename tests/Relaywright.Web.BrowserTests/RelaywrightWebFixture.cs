using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Relaywright.Web.BrowserTests;

public sealed class RelaywrightWebFixture : IAsyncLifetime
{
    private readonly List<string> output = [];
    private Process? process;
    private string? dataDirectory;

    public string BaseUrl { get; private set; } = string.Empty;

    public string UserName { get; private set; } = "browser-admin";

    public string Password { get; private set; } = "BrowserTests!12345";

    public async Task InitializeAsync()
    {
        var externalBaseUrl = Environment.GetEnvironmentVariable("RELAYWRIGHT_BROWSER_BASE_URL");
        if (!string.IsNullOrWhiteSpace(externalBaseUrl))
        {
            BaseUrl = externalBaseUrl.TrimEnd('/');
            UserName = RequireEnvironmentVariable("RELAYWRIGHT_BROWSER_USER_NAME");
            Password = RequireEnvironmentVariable("RELAYWRIGHT_BROWSER_PASSWORD");
            await WaitForApplicationAsync(processToMonitor: null);
            return;
        }

        var repositoryRoot = FindRepositoryRoot();
        var appDll = ResolveApplicationDll(repositoryRoot);
        dataDirectory = Path.Combine(Path.GetTempPath(), $"relaywright-browser-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dataDirectory);

        var port = ReservePort();
        BaseUrl = $"http://127.0.0.1:{port}";
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add(appDll);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(BaseUrl);
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        startInfo.Environment["Storage__DataDirectory"] = dataDirectory;
        startInfo.Environment["BootstrapAdmin__UserName"] = UserName;
        startInfo.Environment["BootstrapAdmin__Email"] = "browser-admin@localhost";
        startInfo.Environment["BootstrapAdmin__Password"] = Password;
        startInfo.Environment["Logging__EventLog__LogLevel__Default"] = "None";

        process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, args) => Capture(args.Data);
        process.ErrorDataReceived += (_, args) => Capture(args.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WaitForApplicationAsync(process);
    }

    private async Task WaitForApplicationAsync(Process? processToMonitor)
    {
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        };
        using var client = new HttpClient(handler);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(40);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (processToMonitor?.HasExited == true)
            {
                throw new InvalidOperationException(
                    $"Relaywright browser-test host exited with code {processToMonitor.ExitCode}.{Environment.NewLine}{RecentOutput()}");
            }

            try
            {
                using var response = await client.GetAsync($"{BaseUrl}/Account/Login");
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // The application has not bound its HTTP listener yet.
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Relaywright did not become ready.{Environment.NewLine}{RecentOutput()}");
    }

    public Task DisposeAsync()
    {
        if (process is { HasExited: false })
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(TimeSpan.FromSeconds(10));
        }

        process?.Dispose();
        if (dataDirectory is not null && Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private void Capture(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        lock (output)
        {
            output.Add(line);
            if (output.Count > 100)
            {
                output.RemoveAt(0);
            }
        }
    }

    private string RecentOutput()
    {
        lock (output)
        {
            return string.Join(Environment.NewLine, output);
        }
    }

    private static int ReservePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string ResolveApplicationDll(string repositoryRoot)
    {
        var configured = Environment.GetEnvironmentVariable("RELAYWRIGHT_BROWSER_APP_DLL");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var candidates = Directory.GetFiles(
                Path.Combine(repositoryRoot, "src", "Relaywright.Web", "bin"),
                "Relaywright.Web.dll",
                SearchOption.AllDirectories)
            .Where(path => path.Contains($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}") ||
                           path.EndsWith($"{Path.DirectorySeparatorChar}net10.0{Path.DirectorySeparatorChar}Relaywright.Web.dll"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        return candidates.FirstOrDefault()
            ?? throw new FileNotFoundException("Build Relaywright.Web before running browser tests.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Relaywright.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Relaywright repository root.");
    }

    private static string RequireEnvironmentVariable(string name)
    {
        return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"{name} is required when RELAYWRIGHT_BROWSER_BASE_URL targets an existing deployment.");
    }
}
