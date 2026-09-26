using System.Diagnostics;

namespace Wandur.Tests;

/// <summary>Temporary directories and file-handle checks shared by the test projects.</summary>
internal static class TestFiles
{
    /// <summary>A new, empty directory under the temp path.</summary>
    public static string CreateDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Deletes a test directory. Windows refuses while any handle is open, so the caller must first dispose
    /// what it opened (a <c>ClientDatabase</c> releases its pooled connections on dispose). macOS and Linux
    /// would delete an open file quietly, so there a file this process still holds fails the same way Windows
    /// does, which lets a leaked handle fail on every platform. The short retry only covers scanners and
    /// indexers that briefly open new files on Windows.
    /// </summary>
    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        if (!OperatingSystem.IsWindows())
        {
            var open = OpenFilesUnder(path);
            if (open.Count > 0)
                throw new IOException("The test left files open, which Windows would refuse to delete: " + string.Join(", ", open));
        }
        for (var attempt = 0; ; attempt++)
        {
            if (!Directory.Exists(path)) return;
            try { Directory.Delete(path, recursive: true); return; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException && attempt < 10)
            { Thread.Sleep(50); }
        }
    }

    /// <summary>
    /// Whether this process still holds the file open. Deleting an open file succeeds on macOS and Linux, so a
    /// delete alone would not catch a leaked handle there; this asks the operating system directly.
    /// </summary>
    public static bool IsOpenByThisProcess(string path)
    {
        path = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
        {
            // SQLite opens with shared read, write and delete; an exclusive open fails while it holds the file.
            try { using var _ = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return false; }
            catch (IOException) { return true; }
        }
        return OpenFiles().Contains(path);
    }

    /// <summary>The files under a directory that this process holds open (macOS and Linux).</summary>
    public static IReadOnlyList<string> OpenFilesUnder(string directory)
    {
        var prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return OpenFiles().Where(file => file.StartsWith(prefix, StringComparison.Ordinal)).ToList();
    }

    private static HashSet<string> OpenFiles()
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        if (OperatingSystem.IsLinux())
        {
            foreach (var descriptor in Directory.EnumerateFileSystemEntries("/proc/self/fd"))
            {
                try { if (new FileInfo(descriptor).LinkTarget is { } target) files.Add(target); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return files;
        }
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        // lsof names each open file on an "n" line, with /var resolved to /private/var.
        var start = new ProcessStartInfo(File.Exists("/usr/sbin/lsof") ? "/usr/sbin/lsof" : "lsof")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-n", "-P", "-Fn", "-p", Environment.ProcessId.ToString() }) start.ArgumentList.Add(argument);
        using var lsof = Process.Start(start) ?? throw new InvalidOperationException("lsof did not start.");
        var errors = lsof.StandardError.ReadToEndAsync();
        var output = lsof.StandardOutput.ReadToEnd();
        lsof.WaitForExit();
        _ = errors.Result;
        foreach (var line in output.Split('\n'))
        {
            if (!line.StartsWith('n')) continue;
            var name = line[1..];
            files.Add(name);
            if (name.StartsWith("/private/", StringComparison.Ordinal)) files.Add(name["/private".Length..]);
        }
        return files;
    }
}
