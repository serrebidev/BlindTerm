using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

    public async Task<UpdateManifest?> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            $"https://github.com/{Repository}/releases/latest/download/{ManifestName}",
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return manifest is not null && IsNewer(manifest.Version, VersionInfo.Current) ? manifest : null;
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

        string asset = Path.GetFileName(manifest.Asset);
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
