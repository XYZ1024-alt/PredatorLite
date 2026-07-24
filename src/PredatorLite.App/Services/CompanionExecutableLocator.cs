using System.IO;

namespace PredatorLite.App.Services;

internal static class CompanionExecutableLocator
{
    public static string? Find(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(path) ? path : null;
    }
}
