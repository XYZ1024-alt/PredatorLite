using System.IO;

namespace PredatorLite.App.Services;

internal static class CompanionExecutableLocator
{
    public static string? Find(string fileName)
    {
        string direct = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(direct))
        {
            return direct;
        }

        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !string.Equals(directory.Name, "src", StringComparison.OrdinalIgnoreCase))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return null;
        }

        string projectName = Path.GetFileNameWithoutExtension(fileName);
        string configuration = AppContext.BaseDirectory.Contains("\\Release\\", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        string candidate = Path.Combine(
            directory.FullName,
            projectName,
            "bin",
            configuration,
            "net10.0-windows10.0.19041.0",
            "win-x64",
            fileName);
        return File.Exists(candidate) ? candidate : null;
    }
}
