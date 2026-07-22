using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace PredatorLite.App.Services;

public interface IUserInteraction
{
    bool Confirm(string message, string title);

    void ShowMessage(string message, string title, bool isError = false);

    string? ChooseDiagnosticsPath();

    string? PickColor(string currentColor);

    void OpenFolder(string path);
}

public sealed class DesktopUserInteraction : IUserInteraction
{
    public bool Confirm(string message, string title) =>
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void ShowMessage(string message, string title, bool isError = false) =>
        System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, isError ? MessageBoxImage.Error : MessageBoxImage.Information);

    public string? ChooseDiagnosticsPath()
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
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickColor(string currentColor)
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

        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}"
            : null;
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
