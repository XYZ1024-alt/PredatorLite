using Microsoft.Win32;
using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.Tests;

public sealed class ProtectedApplicationPathValidatorTests
{
    private static readonly IReadOnlyList<string> ProtectedRoots =
    [
        @"C:\Program Files\",
        @"C:\Program Files (x86)\",
        @"C:\Windows\"
    ];

    private static readonly IReadOnlyList<string> UserWritableRoots =
    [
        @"C:\Users\test\",
        @"C:\Users\test\AppData\Local\",
        @"C:\ProgramData\",
        @"C:\Users\test\AppData\Local\Temp\"
    ];

    [Theory]
    [InlineData(@"C:\Program Files\PredatorLite")]
    [InlineData(@"C:\Program Files\PredatorLite\")]
    [InlineData(@"C:\Program Files (x86)\PredatorLite")]
    [InlineData(@"C:\Program Files (x86)\PredatorLite\")]
    [InlineData(@"C:\Windows\System32\PredatorLite")]
    public void ProgramFilesAndWindowsAreProtected(string directory) =>
        Assert.True(IsProtected(directory));

    [Theory]
    [InlineData(@"C:\Users\test\AppData\Local\Programs\PredatorLite")]
    [InlineData(@"C:\Users\test\Downloads\PredatorLite")]
    [InlineData(@"C:\ProgramData\PredatorLite")]
    [InlineData(@"C:\Users\test\AppData\Local\Temp\PredatorLite")]
    [InlineData(@"C:\Users\test\Desktop")]
    public void UserWritableDirectoriesAreNotProtected(string directory) =>
        Assert.False(IsProtected(directory));

    [Theory]
    [InlineData(@"D:\PredatorLite")]
    [InlineData(@"")]
    [InlineData(" ")]
    public void UnknownRootsAndEmptyPathsAreNotProtected(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.False(IsProtected(directory));
        }
        else
        {
            // On non-Windows CI, Path.GetFullPath may resolve differently;
            // the pure check still rejects unknown roots.
            Assert.False(IsProtected(directory));
        }
    }

    [Theory]
    [InlineData(@"c:\program FILES\predatorLITE")]
    [InlineData(@"C:\PROGRAM FILES\PredatorLite")]
    public void PathComparisonIsCaseInsensitive(string directory) =>
        Assert.True(IsProtected(directory));

    [Fact]
    public void ProgramFilesSubdirectoryMatchIsExactNotPrefix()
    {
        Assert.True(IsProtected(@"C:\Program Files\PredatorLite"));
        Assert.False(IsProtected(@"C:\Program Files Evil\PredatorLite"));
    }

    [Theory]
    [InlineData(@"C:\Program Files\..\..\Users\test\AppData\Local\Programs\PredatorLite")]
    [InlineData(@"C:\Program Files\..\Users\test\PredatorLite")]
    public void TraversalIntoUserWritableRootIsRejected(string directory) =>
        Assert.False(IsProtected(directory));

    private static bool IsProtected(string directory) =>
        ProtectedApplicationPathValidator.IsProtectedApplicationBase(
            directory,
            ProtectedRoots,
            UserWritableRoots);
}
