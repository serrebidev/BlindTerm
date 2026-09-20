using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlindTerm.Core.Updates;

/// <summary>
/// Checks and stages BlindTerm updates. Applying the files is delegated to the executable's
/// update mode, because Windows cannot replace a running program.
/// </summary>
public sealed class UpdateClient : IDisposable
{
    public const string Repository = "serrebidev/BlindTerm";
    public const string ManifestName = "BlindTerm-update.json";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public UpdateClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _ownsClient = http is null;
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("BlindTerm", VersionInfo.Current));
    }

    /// <summary>
    /// The version the last check found, whether or not it was newer than this copy.
    ///
    /// Kept so that "up to date" can be a fact rather than an assumption. A check that fetched
    /// a manifest naming an older release, and a check that could not fetch one at all, used to
    /// arrive at the same sentence -- and the difference is exactly what somebody who has just
    /// published a version needs to know while they wait to see it offered.
    /// </summary>
    public string? LastOffered { get; private set; }

    /// <summary>
    /// Where the newest release's manifest lives, asked of the API rather than assumed.
    ///
    /// The obvious address is releases/latest/download, which is a redirect that GitHub caches
    /// at the edge. For a while after a release is published that cache can still answer with
    /// the *previous* release's manifest, which is read as "you are up to date" -- the release
    /// is live, the tag is live, the API knows about it, and the one address the updater trusts
    /// is still serving the day before. That is what happened to v0.7.11: for several minutes
    /// after publishing, this URL returned v0.7.10's manifest.
    ///
    /// The API is not cached that way, so it is asked first and its tag used to build a direct
    /// asset address. The redirect stays as the fallback for when the API cannot be reached or
    /// is rate-limiting this machine.
    /// </summary>
    private async Task<string> ManifestUrlAsync(CancellationToken cancellationToken)
    {
        const string fallback = $"https://github.com/{Repository}/releases/latest/download/{ManifestName}";
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return fallback;

            await using Stream stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await JsonSerializer
                .DeserializeAsync<ReleaseInfo>(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(release?.Tag)
                ? fallback
                : $"https://github.com/{Repository}/releases/download/{release.Tag}/{ManifestName}";
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException
                                   or TaskCanceledException or InvalidOperationException
                                   or NotSupportedException)
        {
            // A check the caller cancelled is the caller's answer, not a reason to go on and
            // make the second request the redirect costs.
            if (cancellationToken.IsCancellationRequested) throw;
            // Falls through to the redirect, which is what this did before the API existed.
            return fallback;
        }
    }

    public async Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken = default)
    {
        LastOffered = null;

        using var response = await _http.GetAsync(await ManifestUrlAsync(cancellationToken).ConfigureAwait(false),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (manifest is null) return null;

        LastOffered = manifest.Version;
        return IsNewer(manifest.Version, VersionInfo.Current) ? manifest : null;
    }

    /// <summary>The one field this needs out of the API's much larger answer about a release.</summary>
    private sealed record ReleaseInfo
    {
        [JsonPropertyName("tag_name")] public string? Tag { get; init; }
    }

    public async Task<string> DownloadAsync(UpdateManifest manifest, IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // A manifest is a document this program does not control, so nothing in it is taken
        // on trust -- including that the fields are there at all, and that the address is
        // still an ordinary web address on the host it claims to be.
        if (!Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out Uri? source)
            || source.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The update does not name an https download address.");

        string? asset = Path.GetFileName(manifest.Asset);
        if (string.IsNullOrEmpty(asset) || string.IsNullOrWhiteSpace(manifest.Sha256))
            throw new InvalidDataException("The update has no file name or no hash to check it against.");

        string root = Path.Combine(Path.GetTempPath(), "BlindTerm-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string archive = Path.Combine(root, asset);

        using var response = await _http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using (Stream body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var destination = File.Create(archive))
        {
            var buffer = new byte[64 * 1024];
            long total = 0;
            int read;
            while ((read = await body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                progress?.Report(total);
            }
        }

        await using var downloaded = File.OpenRead(archive);
        string actual = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, cancellationToken))
            .ToLowerInvariant();
        if (!HashMatches(actual, manifest.Sha256.Trim()))
        {
            TryDelete(root);
            throw new InvalidDataException($"The update hash does not match. Expected {manifest.Sha256}, got {actual}.");
        }
        return archive;
    }

    /// <summary>
    /// Compares two hex digests without letting the time taken say how much of it matched.
    ///
    /// The digest is not a secret and the manifest is already fetched over TLS, so this is
    /// hardening rather than a fix: it costs three lines and removes the question.
    /// </summary>
    private static bool HashMatches(string actual, string expected)
    {
        byte[] left = Encoding.ASCII.GetBytes(actual.ToLowerInvariant());
        byte[] right = Encoding.ASCII.GetBytes(expected.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(left, right);
    }

    /// <summary>Starts this executable in update mode and returns immediately.</summary>
    public static Process LaunchApply(string archive, int processId, string installDirectory)
    {
        string current = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current executable path is unavailable.");
        string installedHelper = Path.Combine(Path.GetDirectoryName(current) ?? AppContext.BaseDirectory, "BlindTerm.Update.exe");
        if (!File.Exists(installedHelper)) throw new FileNotFoundException("The update worker is not installed.", installedHelper);
        string helper = Path.Combine(Path.GetTempPath(), "BlindTerm-update-worker-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(installedHelper, helper);
        var info = new ProcessStartInfo(helper)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(current) ?? AppContext.BaseDirectory,
        };
        if (NeedsElevation(installDirectory)) info.Verb = "runas";
        info.ArgumentList.Add("--apply-update");
        info.ArgumentList.Add(processId.ToString());
        info.ArgumentList.Add(installDirectory);
        info.ArgumentList.Add(archive);
        info.ArgumentList.Add(Path.GetFileName(current));
        return Process.Start(info) ?? throw new InvalidOperationException("Could not start the update helper.");
    }

    public static bool IsNewer(string? candidate, string current)
        => Normalize(candidate) > Normalize(current);

    /// <summary>
    /// A version, or the lowest one there is when the publisher wrote something unparseable.
    /// Treating a version nobody can read as "not newer" is the safe direction: the worst it
    /// costs is a release that has to be installed by hand.
    /// </summary>
    private static Version Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new Version(0, 0);
        value = value.Trim().TrimStart('v', 'V');
        return Version.TryParse(value, out Version? parsed) ? parsed : new Version(0, 0);
    }

    private static bool NeedsElevation(string directory)
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return directory.TrimEnd(Path.DirectorySeparatorChar).StartsWith(
            programFiles.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    internal static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
