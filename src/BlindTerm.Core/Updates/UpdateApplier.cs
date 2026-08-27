using System.Diagnostics;
using System.IO.Compression;

namespace BlindTerm.Core.Updates;

/// <summary>Runs in a short-lived child process while the main window is closed.</summary>
public static class UpdateApplier
{
    public static int Run(string[] args)
    {
        if (args.Length != 4 || !int.TryParse(args[0], out int pid)) return 2;
        string install = Path.GetFullPath(args[1]);
        string archive = Path.GetFullPath(args[2]);
        string executableName = Path.GetFileName(args[3]);

        try
        {
            ValidatePaths(install, archive, executableName);
            WaitForProcess(pid);
            string staging = Path.Combine(Path.GetTempPath(), "BlindTerm-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                ZipFile.ExtractToDirectory(archive, staging, overwriteFiles: true);
                string source = FindPayloadRoot(staging, executableName);
                ReplaceContents(install, source, executableName);
                Process.Start(new ProcessStartInfo(Path.Combine(install, executableName))
                {
                    UseShellExecute = true,
                    WorkingDirectory = install,
                });
                return 0;
            }
            finally
            {
                UpdateClient.TryDelete(staging);
                UpdateClient.TryDelete(Path.GetDirectoryName(archive) ?? string.Empty);
            }
        }
        catch
        {
            return 1;
        }
    }

    private static void ValidatePaths(string install, string archive, string executableName)
    {
        string root = Path.GetPathRoot(install) ?? string.Empty;
        if (string.Equals(install.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase) || !Directory.Exists(install))
            throw new InvalidDataException("The update target is not a valid application directory.");
        if (string.IsNullOrWhiteSpace(executableName) || executableName != Path.GetFileName(executableName) ||
            executableName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException("The update executable name is invalid.");
        if (!File.Exists(archive) || !archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update archive is invalid.");

        string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string parent = Path.GetFullPath(Path.GetDirectoryName(archive) ?? string.Empty);
        if (!parent.StartsWith(temp, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(parent).StartsWith("BlindTerm-update-", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update archive is not in a BlindTerm staging directory.");
    }

    private static void WaitForProcess(int pid)
    {
        try
        {
            using Process process = Process.GetProcessById(pid);
            if (!process.WaitForExit(30_000)) process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { }
    }

    private static string FindPayloadRoot(string staging, string executableName)
    {
        if (File.Exists(Path.Combine(staging, executableName))) return staging;
        string[] directories = Directory.GetDirectories(staging);
        if (directories.Length == 1 && File.Exists(Path.Combine(directories[0], executableName))) return directories[0];
        throw new InvalidDataException("The update archive has no application executable.");
    }

    internal static void ReplaceContents(string install, string source, string executableName)
    {
        string backup = install.TrimEnd(Path.DirectorySeparatorChar) + ".previous-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        Directory.CreateDirectory(backup);
        bool installed = false;
        try
        {
            foreach (string path in Directory.GetFileSystemEntries(install))
            {
                string name = Path.GetFileName(path);
                if (Preserved(name)) continue;
                MoveEntry(path, Path.Combine(backup, name));
            }
            foreach (string path in Directory.GetFileSystemEntries(source))
            {
                string name = Path.GetFileName(path);
                if (Preserved(name)) continue;
                MoveEntry(path, Path.Combine(install, name));
            }
            installed = true;
        }
        catch
        {
            try
            {
                foreach (string path in Directory.GetFileSystemEntries(install))
                    if (!Preserved(Path.GetFileName(path))) UpdateClient.TryDelete(path);
                foreach (string path in Directory.GetFileSystemEntries(backup))
                    MoveEntry(path, Path.Combine(install, Path.GetFileName(path)));
            }
            catch
            {
                // Keep the backup if rollback itself encountered a locked file.
            }
            throw;
        }
        finally
        {
            if (installed) UpdateClient.TryDelete(backup);
        }
    }

    private static bool Preserved(string name)
        => name.Equals("settings.json", StringComparison.OrdinalIgnoreCase)
           || name.Equals(".windows-installed", StringComparison.OrdinalIgnoreCase);

    private static void MoveEntry(string source, string target)
    {
        if (Directory.Exists(source)) Directory.Move(source, target);
        else File.Move(source, target, overwrite: true);
    }
}
