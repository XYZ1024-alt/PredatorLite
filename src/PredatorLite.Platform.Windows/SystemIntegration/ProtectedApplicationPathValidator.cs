using System.Runtime.InteropServices;

namespace PredatorLite.Platform.Windows.SystemIntegration;

/// <summary>
/// Validates that the elevated companion executable resides in a directory
/// that an unprivileged same-user process cannot modify. The elevated helper
/// crosses the UAC boundary via <c>runas</c>; launching it from a user-writable
/// directory would allow a non-administrator to substitute arbitrary code that
/// runs with administrator rights.
/// </summary>
public static class ProtectedApplicationPathValidator
{
    private const int ErrorAccessDenied = 5;

    private static readonly string Separator = Path.DirectorySeparatorChar.ToString();

    public static bool IsCompanionExecutableTrusted(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        string? resolved = ResolveCanonicalExecutablePath(executablePath);
        if (resolved is null)
        {
            return false;
        }

        string? directory = Path.GetDirectoryName(resolved);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        if (!IsProtectedApplicationBase(directory))
        {
            return false;
        }

        return !IsDirectoryWritableByCurrentUser(directory);
    }

    /// <summary>
    /// Determines whether <paramref name="baseDirectory"/> is located beneath a
    /// system-protected root (Program Files or Windows) and outside every
    /// user-writable root (user profile, LocalAppData, ProgramData, Temp).
    /// </summary>
    public static bool IsProtectedApplicationBase(string baseDirectory) =>
        IsProtectedApplicationBase(
            baseDirectory,
            ResolveProtectedRoots(),
            ResolveUserWritableRoots());

    /// <summary>
    /// Pure overload for unit testing: the caller supplies both the protected
    /// and user-writable root lists so no environment access is required.
    /// </summary>
    public static bool IsProtectedApplicationBase(
        string baseDirectory,
        IReadOnlyList<string> protectedRoots,
        IReadOnlyList<string> userWritableRoots)
    {
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            return false;
        }

        string normalized;
        try
        {
            normalized = Path.GetFullPath(baseDirectory);
        }
        catch (Exception)
        {
            return false;
        }

        if (!normalized.EndsWith(Separator, StringComparison.Ordinal))
        {
            normalized += Separator;
        }

        foreach (string root in userWritableRoots)
        {
            if (StartsWithRoot(normalized, root))
            {
                return false;
            }
        }

        foreach (string root in protectedRoots)
        {
            if (StartsWithRoot(normalized, root))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to create a uniquely-named file inside <paramref name="directoryPath"/>.
    /// Returns <c>true</c> when the current user has write permission (an unsafe
    /// location for an elevated companion); <c>false</c> when the operating system
    /// denies the write (a protected location).
    /// </summary>
    public static bool IsDirectoryWritableByCurrentUser(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
        {
            return false;
        }

        string probePath = Path.Combine(directoryPath, $".predatorlite-write-probe-{Guid.NewGuid():N}");

        IntPtr handle = CreateFileW(
            probePath,
            access: GenericWrite,
            share: FileShareNone,
            securityAttributes: IntPtr.Zero,
            creationDisposition: CreateNew,
            flagsAndAttributes: FileAttributeNormal,
            templateFile: IntPtr.Zero);

        if (handle != InvalidHandle)
        {
            CloseHandle(handle);
            DeleteFileW(probePath);
            return true;
        }

        int error = Marshal.GetLastWin32Error();
        return error != ErrorAccessDenied;
    }

    private static string? ResolveCanonicalExecutablePath(string executablePath)
    {
        try
        {
            if (!File.Exists(executablePath))
            {
                return null;
            }

            FileInfo info = new(executablePath);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return null;
            }

            return Path.GetFullPath(executablePath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool StartsWithRoot(string normalizedPath, string root)
    {
        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(root);
        }
        catch (Exception)
        {
            return false;
        }

        if (!normalizedRoot.EndsWith(Separator, StringComparison.Ordinal))
        {
            normalizedRoot += Separator;
        }

        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ResolveProtectedRoots()
    {
        List<string> roots = new(4);

        AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));

        string? windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            AddIfExists(roots, windows);
        }

        return roots;
    }

    public static IReadOnlyList<string> ResolveUserWritableRoots()
    {
        List<string> roots = new(4);

        AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        AddIfExists(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        string? temp = Path.GetTempPath();
        if (!string.IsNullOrWhiteSpace(temp))
        {
            AddIfExists(roots, temp);
        }

        return roots;
    }

    private static void AddIfExists(List<string> roots, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            roots.Add(path);
        }
    }

    private const uint GenericWrite = 0x40000000;
    private const uint FileShareNone = 0;
    private const uint CreateNew = 1;
    private const uint FileAttributeNormal = 0x80;
    private static readonly IntPtr InvalidHandle = new(-1);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFileW(
        string fileName,
        uint access,
        uint share,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFileW(string fileName);
}
