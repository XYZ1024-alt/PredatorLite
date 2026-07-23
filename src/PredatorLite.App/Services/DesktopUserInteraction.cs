using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PredatorLite.Core.Abstractions;
using Windows.Storage.Pickers;
using Windows.UI;

namespace PredatorLite.App.Services;

public enum ConfirmationKind
{
    Standard,
    RebootRequired,
    Destructive
}

public interface IUserInteraction
{
    Task<bool> ConfirmAsync(
        string message,
        string title,
        ConfirmationKind kind = ConfirmationKind.Standard);

    Task<string?> ChooseDiagnosticsPathAsync();

    Task<string?> PickColorAsync(string currentColor);

    void OpenFolder(string path);
}

public sealed class DesktopUserInteraction(
    Func<XamlRoot?> xamlRootProvider,
    Func<IntPtr> windowHandleProvider,
    LocalizationService localization,
    IAppLogger logger) : IUserInteraction
{
    private readonly SemaphoreSlim _dialogGate = new(1, 1);

    public async Task<bool> ConfirmAsync(
        string message,
        string title,
        ConfirmationKind kind = ConfirmationKind.Standard)
    {
        XamlRoot? root = xamlRootProvider();
        if (root is null)
        {
            logger.Error("A confirmation dialog was requested before the window was ready.");
            return false;
        }

        await _dialogGate.WaitAsync();
        try
        {
            ContentDialog dialog = new()
            {
                XamlRoot = root,
                Title = title,
                Content = new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 460
                },
                PrimaryButtonText = localization.Get("Action.Confirm"),
                CloseButtonText = localization.Get("Action.Cancel"),
                DefaultButton = kind == ConfirmationKind.Standard
                    ? ContentDialogButton.Primary
                    : ContentDialogButton.Close
            };
            AutomationProperties.SetAutomationId(dialog, $"ConfirmationDialog.{kind}");
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _dialogGate.Release();
        }
    }

    public async Task<string?> ChooseDiagnosticsPathAsync()
    {
        IntPtr handle = windowHandleProvider();
        if (handle == IntPtr.Zero)
        {
            logger.Error("The diagnostics picker was requested before the window was ready.");
            return null;
        }

        FileSavePicker picker = new()
        {
            SuggestedFileName = $"PredatorLite-Diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}"
        };
        picker.FileTypeChoices.Add(localization.Get("FileType.ZipArchive"), [".zip"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickColorAsync(string currentColor)
    {
        XamlRoot? root = xamlRootProvider();
        if (root is null)
        {
            logger.Error("The color picker was requested before the window was ready.");
            return null;
        }

        ColorPicker picker = new()
        {
            Color = ParseColor(currentColor),
            IsAlphaEnabled = false,
            IsAlphaSliderVisible = false,
            IsAlphaTextInputVisible = false,
            IsColorChannelTextInputVisible = true,
            IsHexInputVisible = true,
            IsMoreButtonVisible = true
        };
        AutomationProperties.SetAutomationId(picker, "LightingColorPicker");

        await _dialogGate.WaitAsync();
        try
        {
            ContentDialog dialog = new()
            {
                XamlRoot = root,
                Title = localization.Get("Tip.PickColor"),
                Content = picker,
                PrimaryButtonText = localization.Get("Action.Confirm"),
                CloseButtonText = localization.Get("Action.Cancel"),
                DefaultButton = ContentDialogButton.Primary
            };
            AutomationProperties.SetAutomationId(dialog, "LightingColorDialog");
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return null;
            }

            Color color = picker.Color;
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        finally
        {
            _dialogGate.Release();
        }
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

    private static Color ParseColor(string value)
    {
        try
        {
            string hex = value.TrimStart('#');
            return Color.FromArgb(
                255,
                byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        catch
        {
            return Color.FromArgb(255, 0, 168, 232);
        }
    }
}
