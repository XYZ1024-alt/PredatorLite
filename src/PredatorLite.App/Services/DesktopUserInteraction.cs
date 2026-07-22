using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace PredatorLite.App.Services;

public interface IUserInteraction
{
    Task<bool> ConfirmAsync(string message, string title);

    Task<string?> ChooseDiagnosticsPathAsync();

    Task<string?> PickColorAsync(string currentColor);

    void OpenFolder(string path);
}

public sealed class DesktopUserInteraction : IUserInteraction
{
    public Task<bool> ConfirmAsync(string message, string title) =>
        Task.FromResult(
            System.Windows.MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) ==
            MessageBoxResult.Yes);

    public Task<string?> ChooseDiagnosticsPathAsync()
    {
        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = "PredatorLite Diagnostics",
            FileName = $"PredatorLite-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.zip",
            DefaultExt = ".zip",
            Filter = "ZIP archive (*.zip)|*.zip",
            AddExtension = true,
            OverwritePrompt = true
        };
        return Task.FromResult(dialog.ShowDialog() == true ? dialog.FileName : null);
    }

    public Task<string?> PickColorAsync(string currentColor)
    {
        using System.Windows.Forms.ColorDialog dialog = new()
        {
            FullOpen = true,
            AnyColor = true
        };
        try
        {
            dialog.Color = System.Drawing.ColorTranslator.FromHtml(currentColor);
        }
        catch
        {
            dialog.Color = System.Drawing.Color.DeepSkyBlue;
        }

        string? result = dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}"
            : null;
        return Task.FromResult(result);
    }

    public void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
