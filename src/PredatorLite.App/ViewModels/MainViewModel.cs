using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PredatorLite.App.Services;
using PredatorLite.Core.Abstractions;
using PredatorLite.Core.Models;
using PredatorLite.Core.Services;

namespace PredatorLite.App.ViewModels;

public partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly OperatingMode[] CycleModes =
    [
        OperatingMode.Silent,
        OperatingMode.Balanced,
        OperatingMode.Performance,
        OperatingMode.Turbo
    ];

    private readonly IPredatorPlatform _platform;
    private readonly ISettingsStore _settingsStore;
    private readonly IAppLogger _logger;
    private readonly IModeKeySource _modeKeySource;
    private readonly IFpsSource _fpsSource;
    private readonly FanGuardClient _fanGuard;
    private readonly StartupManager _startupManager;
    private readonly ElevatedHelperLauncher _elevatedHelper;
    private readonly DiagnosticsExporter _diagnosticsExporter;
    private readonly LocalizationService _localization;
    private readonly IUserInteraction _interaction;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly SemaphoreSlim _hardwareGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();

    private AppSettings _settings = new();
    private DeviceCapabilities? _capabilities;
    private HardwareSnapshot _snapshot = new();
    private IReadOnlyDictionary<DeviceSettingId, DeviceSettingState> _deviceSettingStates =
        new Dictionary<DeviceSettingId, DeviceSettingState>();
    private Task? _monitorTask;
    private bool? _lastAcState;
    private bool _customFanActive;
    private FanCurve? _activeFanCurve;
    private int _lastCpuFanTarget = -1;
    private int _lastGpuFanTarget = -1;
    private DateTimeOffset _lastFanWrite = DateTimeOffset.MinValue;
    private bool _disposed;

    public MainViewModel(
        IPredatorPlatform platform,
        ISettingsStore settingsStore,
        IAppLogger logger,
        IModeKeySource modeKeySource,
        IFpsSource fpsSource,
        FanGuardClient fanGuard,
        StartupManager startupManager,
        ElevatedHelperLauncher elevatedHelper,
        DiagnosticsExporter diagnosticsExporter,
        LocalizationService localization,
        IUserInteraction interaction,
        IUiDispatcher uiDispatcher)
    {
        _platform = platform;
        _settingsStore = settingsStore;
        _logger = logger;
        _modeKeySource = modeKeySource;
        _fpsSource = fpsSource;
        _fanGuard = fanGuard;
        _startupManager = startupManager;
        _elevatedHelper = elevatedHelper;
        _diagnosticsExporter = diagnosticsExporter;
        _localization = localization;
        _interaction = interaction;
        _uiDispatcher = uiDispatcher;
        StatusMessage = "PredatorLite";
    }

    public ObservableCollection<FanCurvePointViewModel> CpuFanPoints { get; } = [];

    public ObservableCollection<FanCurvePointViewModel> GpuFanPoints { get; } = [];

    public ObservableCollection<LightingZoneViewModel> LightingZones { get; } = [];

    public ObservableCollection<SelectionOption<LightingEffect>> LightingEffects { get; } = [];

    public ObservableCollection<DeviceSettingItemViewModel> DeviceSettings { get; } = [];

    public ObservableCollection<ManagedServiceInfo> ManagedServices { get; } = [];

    public ObservableCollection<int> RefreshRates { get; } = [];

    public IReadOnlyList<int> LightingDirections { get; } = [1, 2, 3, 4];

    [ObservableProperty]
    public partial bool IsInitialized { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial string CurrentLanguage { get; set; } = "zh-CN";

    [ObservableProperty]
    public partial string DeviceModel { get; set; } = "--";

    [ObservableProperty]
    public partial string BiosVersion { get; set; } = "--";

    [ObservableProperty]
    public partial string CompatibilityMessage { get; set; } = "--";

    [ObservableProperty]
    public partial bool HardwareWritesEnabled { get; set; }

    [ObservableProperty]
    public partial bool FanControlAvailable { get; set; }

    [ObservableProperty]
    public partial bool GpuMuxAvailable { get; set; }

    [ObservableProperty]
    public partial bool LightingAvailable { get; set; }

    [ObservableProperty]
    public partial bool BatteryControlAvailable { get; set; }

    [ObservableProperty]
    public partial bool DisplayControlAvailable { get; set; }

    [ObservableProperty]
    public partial bool AcerServiceAvailable { get; set; }

    [ObservableProperty]
    public partial bool AcerWmiAvailable { get; set; }

    [ObservableProperty]
    public partial OperatingMode? CurrentOperatingMode { get; set; }

    [ObservableProperty]
    public partial FanMode? CurrentFanMode { get; set; }

    [ObservableProperty]
    public partial GpuMuxMode? CurrentGpuMuxMode { get; set; }

    [ObservableProperty]
    public partial string CurrentOperatingModeName { get; set; } = "--";

    [ObservableProperty]
    public partial string CurrentFanModeName { get; set; } = "--";

    [ObservableProperty]
    public partial string CurrentGpuMuxModeName { get; set; } = "--";

    [ObservableProperty]
    public partial bool FanGuardActive { get; set; }

    [ObservableProperty]
    public partial bool RebootRequired { get; set; }

    [ObservableProperty]
    public partial string CpuTemperatureText { get; set; } = "--";

    [ObservableProperty]
    public partial string GpuTemperatureText { get; set; } = "--";

    [ObservableProperty]
    public partial string CpuLoadText { get; set; } = "--";

    [ObservableProperty]
    public partial string GpuLoadText { get; set; } = "--";

    [ObservableProperty]
    public partial string CpuFanText { get; set; } = "--";

    [ObservableProperty]
    public partial string GpuFanText { get; set; } = "--";

    [ObservableProperty]
    public partial string CpuPowerText { get; set; } = "--";

    [ObservableProperty]
    public partial string GpuPowerText { get; set; } = "--";

    [ObservableProperty]
    public partial string CpuClockText { get; set; } = "--";

    [ObservableProperty]
    public partial string GpuClockText { get; set; } = "--";

    [ObservableProperty]
    public partial string MemoryText { get; set; } = "--";

    [ObservableProperty]
    public partial string VramText { get; set; } = "--";

    [ObservableProperty]
    public partial string BatteryText { get; set; } = "--";

    [ObservableProperty]
    public partial string PowerSourceText { get; set; } = "--";

    [ObservableProperty]
    public partial string RefreshRateText { get; set; } = "--";

    [ObservableProperty]
    public partial string FpsText { get; set; } = "--";

    [ObservableProperty]
    public partial int SelectedRefreshRate { get; set; }

    [ObservableProperty]
    public partial bool OverdriveEnabled { get; set; }

    [ObservableProperty]
    public partial bool ChargeLimitEnabled { get; set; }

    [ObservableProperty]
    public partial bool RunAtStartup { get; set; }

    [ObservableProperty]
    public partial bool StartMinimized { get; set; }

    [ObservableProperty]
    public partial bool AutoEcoOnBattery { get; set; }

    [ObservableProperty]
    public partial bool AutoRefreshRate { get; set; }

    [ObservableProperty]
    public partial bool ShowOsd { get; set; }

    [ObservableProperty]
    public partial bool ShowFps { get; set; }

    [ObservableProperty]
    public partial bool EnableGlobalHotkeys { get; set; } = true;

    [ObservableProperty]
    public partial LightingEffect SelectedLightingEffect { get; set; }

    [ObservableProperty]
    public partial int LightingBrightness { get; set; } = 5;

    [ObservableProperty]
    public partial int LightingSpeed { get; set; } = 3;

    [ObservableProperty]
    public partial int LightingDirection { get; set; } = 1;

    [ObservableProperty]
    public partial string LightingPrimaryColor { get; set; } = "#00A8E8";

    [ObservableProperty]
    public partial bool LogoLightingEnabled { get; set; } = true;

    public async Task InitializeAsync()
    {
        if (IsInitialized || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusIsError = false;
        try
        {
            _settings = await _settingsStore.LoadAsync(_lifetime.Token);
            _localization.SetLanguage(_settings.Language);
            CurrentLanguage = _localization.CurrentLanguage;
            ApplySettingsToView();
            UpdateExtendedTelemetryState();
            StatusMessage = _localization.Get("Status.Probing");

            _capabilities = await _platform.ProbeAsync(_lifetime.Token);
            ApplyCapabilities(_capabilities);
            _deviceSettingStates = _capabilities.DeviceSettings;
            RebuildDeviceSettings();
            await RefreshServicesCoreAsync(_lifetime.Token);

            _snapshot = await _platform.ReadSnapshotAsync(_lifetime.Token);
            ApplySnapshot(_snapshot);
            _lastAcState = _snapshot.IsOnAcPower;
            if (_snapshot.DisplayRefreshRate is int currentRate && RefreshRates.Contains(currentRate))
            {
                SelectedRefreshRate = _settings.PreferredRefreshRate is int preferred && RefreshRates.Contains(preferred)
                    ? preferred
                    : currentRate;
            }
            else if (RefreshRates.Count > 0)
            {
                SelectedRefreshRate = RefreshRates[^1];
            }

            _modeKeySource.ModeKeyPressed += OnModeKeyPressed;
            await _modeKeySource.StartAsync(_lifetime.Token);
            if (ShowFps && !await _fpsSource.StartAsync(_lifetime.Token))
            {
                ShowFps = false;
            }

            IsInitialized = true;
            StatusMessage = HardwareWritesEnabled
                ? _localization.Get("Status.Ready")
                : CompatibilityMessage;
            _monitorTask = MonitorAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error("Application initialization failed", exception);
            StatusIsError = true;
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task CycleModeFromShortcutAsync() => CycleOperatingModeCoreAsync();

    [RelayCommand]
    private async Task SetOperatingModeAsync(OperatingMode mode)
    {
        if (CurrentOperatingMode == mode)
        {
            return;
        }

        await _hardwareGate.WaitAsync(_lifetime.Token);
        IsBusy = true;
        try
        {
            if (mode is OperatingMode.Silent or OperatingMode.Eco && _customFanActive)
            {
                ApplyResult autoResult = await _platform.SetFanModeAsync(
                    FanMode.Auto,
                    cancellationToken: _lifetime.Token);
                if (!autoResult.IsSuccess)
                {
                    OnPropertyChanged(nameof(CurrentOperatingMode));
                    PublishResult(autoResult);
                    return;
                }

                await _fanGuard.StopAsync();
                _customFanActive = false;
                _activeFanCurve = null;
                FanGuardActive = false;
                UpdateExtendedTelemetryState();
            }

            ApplyResult result = await _platform.SetOperatingModeAsync(mode, _lifetime.Token);
            if (result.IsSuccess)
            {
                CurrentOperatingMode = mode;
                CurrentOperatingModeName = LocalizeMode(mode);
                if (_snapshot.IsOnAcPower != false && mode != OperatingMode.Eco)
                {
                    _settings.LastAcMode = mode;
                }

                await SaveSettingsAsync();
            }
            else
            {
                OnPropertyChanged(nameof(CurrentOperatingMode));
            }

            PublishResult(result);
        }
        finally
        {
            IsBusy = false;
            _hardwareGate.Release();
        }
    }

    [RelayCommand]
    private Task CycleOperatingModeAsync() => CycleOperatingModeCoreAsync();

    private async Task CycleOperatingModeCoreAsync()
    {
        OperatingMode current = CurrentOperatingMode ?? OperatingMode.Balanced;
        int index = Array.IndexOf(CycleModes, current);
        OperatingMode next = CycleModes[(index + 1 + CycleModes.Length) % CycleModes.Length];
        await SetOperatingModeAsync(next);
    }

    [RelayCommand]
    private async Task SetFanModeAsync(FanMode mode)
    {
        if (mode == FanMode.Custom)
        {
            await ApplyCustomFanAsync();
            return;
        }

        await _hardwareGate.WaitAsync(_lifetime.Token);
        IsBusy = true;
        try
        {
            bool guardWasActive = _fanGuard.IsActive;
            if (mode == FanMode.Max && !await _fanGuard.StartAsync(_lifetime.Token))
            {
                OnPropertyChanged(nameof(CurrentFanMode));
                PublishError(_localization.Get("Status.FanGuardFailed"));
                return;
            }

            ApplyResult result = await _platform.SetFanModeAsync(mode, cancellationToken: _lifetime.Token);
            if (result.IsSuccess)
            {
                _customFanActive = false;
                _activeFanCurve = null;
                CurrentFanMode = mode;
                CurrentFanModeName = LocalizeFanMode(mode);
                _settings.FanMode = mode;
                if (mode == FanMode.Auto)
                {
                    await _fanGuard.StopAsync();
                }

                FanGuardActive = mode == FanMode.Max && _fanGuard.IsActive;
                UpdateExtendedTelemetryState();
                await SaveSettingsAsync();
            }
            else if (mode == FanMode.Max)
            {
                if (!guardWasActive)
                {
                    await _fanGuard.StopAsync();
                }

                FanGuardActive = _fanGuard.IsActive;
            }

            if (!result.IsSuccess)
            {
                OnPropertyChanged(nameof(CurrentFanMode));
            }

            PublishResult(result);
        }
        finally
        {
            IsBusy = false;
            _hardwareGate.Release();
        }
    }

    [RelayCommand]
    private async Task ApplyCustomFanAsync()
    {
        FanCurve curve = BuildFanCurve();
        IReadOnlyList<string> errors = FanCurveEngine.Validate(curve);
        if (errors.Count > 0)
        {
            OnPropertyChanged(nameof(CurrentFanMode));
            PublishError(string.Join(Environment.NewLine, errors));
            return;
        }

        await _hardwareGate.WaitAsync(_lifetime.Token);
        IsBusy = true;
        try
        {
            bool wasCustomActive = _customFanActive && _activeFanCurve is not null;
            FanCurve? previousCurve = _activeFanCurve;
            _platform.SetExtendedTelemetryEnabled(true);
            _snapshot = await _platform.ReadSnapshotAsync(_lifetime.Token);
            ApplySnapshot(_snapshot);
            if (!await _fanGuard.StartAsync(_lifetime.Token))
            {
                UpdateExtendedTelemetryState();
                OnPropertyChanged(nameof(CurrentFanMode));
                PublishError(_localization.Get("Status.FanGuardFailed"));
                return;
            }

            (int cpu, int gpu) = EvaluateFanTargets(curve, _snapshot);
            ApplyResult result = await _platform.SetFanModeAsync(
                FanMode.Custom,
                cpu,
                gpu,
                _lifetime.Token);
            if (result.IsSuccess)
            {
                _settings.FanCurve = curve;
                _settings.FanMode = FanMode.Custom;
                _customFanActive = true;
                _activeFanCurve = curve;
                _lastCpuFanTarget = cpu;
                _lastGpuFanTarget = gpu;
                _lastFanWrite = DateTimeOffset.UtcNow;
                CurrentFanMode = FanMode.Custom;
                CurrentFanModeName = LocalizeFanMode(FanMode.Custom);
                FanGuardActive = true;
                UpdateExtendedTelemetryState();
                await SaveSettingsAsync();
            }
            else
            {
                if (wasCustomActive)
                {
                    _customFanActive = true;
                    _activeFanCurve = previousCurve;
                    FanGuardActive = _fanGuard.IsActive;
                }
                else
                {
                    _customFanActive = false;
                    _activeFanCurve = null;
                    await _fanGuard.StopAsync();
                    FanGuardActive = false;
                }

                UpdateExtendedTelemetryState();
                OnPropertyChanged(nameof(CurrentFanMode));
            }

            PublishResult(result);
        }
        finally
        {
            IsBusy = false;
            _hardwareGate.Release();
        }
    }

    [RelayCommand]
    private async Task SetGpuMuxModeAsync(GpuMuxMode mode)
    {
        if (CurrentGpuMuxMode == mode)
        {
            return;
        }

        string confirmation = string.Format(
            CultureInfo.CurrentCulture,
            _localization.Get("Confirm.Mux"),
            LocalizeMuxMode(mode));
        if (!await _interaction.ConfirmAsync(confirmation, _localization.Get("App.Name")))
        {
            OnPropertyChanged(nameof(CurrentGpuMuxMode));
            return;
        }

        await _hardwareGate.WaitAsync(_lifetime.Token);
        IsBusy = true;
        try
        {
            ApplyResult result = await _platform.SetGpuMuxModeAsync(mode, _lifetime.Token);
            if (result.IsSuccess)
            {
                CurrentGpuMuxMode = mode;
                CurrentGpuMuxModeName = LocalizeMuxMode(mode);
            }
            else
            {
                OnPropertyChanged(nameof(CurrentGpuMuxMode));
            }

            PublishResult(result);
        }
        finally
        {
            IsBusy = false;
            _hardwareGate.Release();
        }
    }

    [RelayCommand]
    private async Task SetChargeLimitAsync(bool enabled)
    {
        ChargeLimitEnabled = enabled;
        await _hardwareGate.WaitAsync(_lifetime.Token);
        IsBusy = true;
        try
        {
            ApplyResult result = await _platform.SetChargeLimitAsync(enabled, _lifetime.Token);
            if (result.IsSuccess)
            {
                _settings.ChargeLimit80Percent = enabled;
                await SaveSettingsAsync();
            }
            else
            {
                ChargeLimitEnabled = !enabled;
            }

            PublishResult(result);
        }
        finally
        {
            IsBusy = false;
            _hardwareGate.Release();
        }
    }

    [RelayCommand]
    private async Task ApplyDisplayAsync()
    {
        if (SelectedRefreshRate <= 0)
        {
            return;
        }

        await _hardwareGate.WaitAsync(_lifetime.Token);
        IsBusy = true;
        try
        {
            ApplyResult result = await _platform.SetRefreshRateAsync(
                SelectedRefreshRate,
                OverdriveEnabled,
                _lifetime.Token);
            if (result.IsSuccess)
            {
                _settings.PreferredRefreshRate = SelectedRefreshRate;
                RefreshRateText = $"{SelectedRefreshRate} Hz";
                await SaveSettingsAsync();
            }

            PublishResult(result);
        }
        finally
        {
            IsBusy = false;
            _hardwareGate.Release();
        }
    }

    [RelayCommand]
    private async Task PickPrimaryColorAsync()
    {
        string? selected = await _interaction.PickColorAsync(LightingPrimaryColor);
        if (selected is not null)
        {
            LightingPrimaryColor = selected;
        }
    }

    [RelayCommand]
    private async Task PickZoneColorAsync(LightingZoneViewModel zone)
    {
        string? selected = await _interaction.PickColorAsync(zone.Color);
        if (selected is not null)
        {
            zone.Color = selected;
        }
    }

    [RelayCommand]
    private async Task ApplyLightingAsync()
    {
        LightingProfile profile = BuildLightingProfile();
        await _hardwareGate.WaitAsync(_lifetime.Token);
        IsBusy = true;
        try
        {
            ApplyResult result = await _platform.SetLightingAsync(profile, _lifetime.Token);
            if (result.IsSuccess)
            {
                _settings.Lighting = profile;
                await SaveSettingsAsync();
            }

            PublishResult(result);
        }
        finally
        {
            IsBusy = false;
            _hardwareGate.Release();
        }
    }

    [RelayCommand]
    private async Task ToggleDeviceSettingAsync(DeviceSettingItemViewModel item)
    {
        bool requested = item.Enabled;
        await _hardwareGate.WaitAsync(_lifetime.Token);
        IsBusy = true;
        try
        {
            ApplyResult result = await _platform.SetDeviceSettingAsync(item.Id, requested, _lifetime.Token);
            if (result.IsSuccess)
            {
                _settings.DeviceSettings[item.Id] = requested;
                await SaveSettingsAsync();
                _deviceSettingStates = await _platform.ReadDeviceSettingsAsync(_lifetime.Token);
                RebuildDeviceSettings();
            }
            else
            {
                item.Enabled = !requested;
            }

            PublishResult(result);
        }
        finally
        {
            IsBusy = false;
            _hardwareGate.Release();
        }
    }

    [RelayCommand]
    private async Task ChangeLanguageAsync(string language)
    {
        _localization.SetLanguage(language);
        CurrentLanguage = _localization.CurrentLanguage;
        _settings.Language = CurrentLanguage;
        RebuildLocalizedValues();
        await SaveSettingsAsync();
        StatusMessage = _localization.Get("Status.Ready");
    }

    [RelayCommand]
    private async Task SavePreferencesAsync()
    {
        _settings.AutoEcoOnBattery = AutoEcoOnBattery;
        _settings.AutoRefreshRate = AutoRefreshRate;
        _settings.StartMinimized = StartMinimized;
        _settings.EnableGlobalHotkeys = EnableGlobalHotkeys;
        _settings.ShowOsd = ShowOsd;
        await SaveSettingsAsync();
        StatusIsError = false;
        StatusMessage = _localization.Get("Status.SettingsSaved");
    }

    [RelayCommand]
    private async Task SetRunAtStartupAsync(bool enabled)
    {
        RunAtStartup = enabled;
        if (!_startupManager.SetEnabled(enabled))
        {
            RunAtStartup = !enabled;
            PublishError(_localization.Get("Status.StartupFailed"));
            return;
        }

        _settings.RunAtStartup = enabled;
        await SaveSettingsAsync();
        StatusIsError = false;
        StatusMessage = _localization.Get("Status.SettingsSaved");
    }

    [RelayCommand]
    private async Task SetFpsEnabledAsync(bool enabled)
    {
        ShowFps = enabled;
        bool applied = enabled
            ? await _fpsSource.StartAsync(_lifetime.Token)
            : await StopFpsAsync();
        if (!applied)
        {
            ShowFps = false;
            PublishError(_localization.Get("Status.FpsFailed"));
        }

        _settings.ShowFps = ShowFps;
        await SaveSettingsAsync();
    }

    [RelayCommand]
    private async Task RefreshServicesAsync()
    {
        IsBusy = true;
        try
        {
            await RefreshServicesCoreAsync(_lifetime.Token);
            StatusIsError = false;
            StatusMessage = _localization.Get("Status.ServicesRefreshed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisableConflictingServicesAsync()
    {
        if (!await _interaction.ConfirmAsync(
                _localization.Get("Confirm.DisableServices"),
                _localization.Get("App.Name")))
        {
            return;
        }

        IsBusy = true;
        try
        {
            ApplyResult result = await _elevatedHelper.SetConflictingServicesDisabledAsync(true);
            PublishResult(result);
            await RefreshServicesCoreAsync(_lifetime.Token);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreConflictingServicesAsync()
    {
        IsBusy = true;
        try
        {
            ApplyResult result = await _elevatedHelper.SetConflictingServicesDisabledAsync(false);
            PublishResult(result);
            await RefreshServicesCoreAsync(_lifetime.Token);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        if (_capabilities is null)
        {
            return;
        }

        string? path = await _interaction.ChooseDiagnosticsPathAsync();
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _diagnosticsExporter.ExportAsync(
                path,
                _capabilities,
                _snapshot,
                _deviceSettingStates,
                ManagedServices.ToArray(),
                _settings,
                _logger.LogDirectory,
                _lifetime.Token);
            StatusIsError = false;
            StatusMessage = _localization.Get("Status.DiagnosticsExported");
        }
        catch (Exception exception)
        {
            _logger.Error("Diagnostics export failed", exception);
            PublishError(exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenLogs() => _interaction.OpenFolder(_logger.LogDirectory);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _modeKeySource.ModeKeyPressed -= OnModeKeyPressed;
        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        try
        {
            await SaveSettingsAsync().ConfigureAwait(false);
        }
        catch
        {
        }

        await _fpsSource.DisposeAsync().ConfigureAwait(false);
        await _modeKeySource.DisposeAsync().ConfigureAwait(false);
        await _fanGuard.DisposeAsync().ConfigureAwait(false);
        await _platform.DisposeAsync().ConfigureAwait(false);
        _hardwareGate.Dispose();
        _lifetime.Dispose();
    }

    private void ApplySettingsToView()
    {
        RunAtStartup = _startupManager.IsEnabled();
        StartMinimized = _settings.StartMinimized;
        AutoEcoOnBattery = _settings.AutoEcoOnBattery;
        AutoRefreshRate = _settings.AutoRefreshRate;
        ShowOsd = _settings.ShowOsd;
        ShowFps = _settings.ShowFps;
        EnableGlobalHotkeys = _settings.EnableGlobalHotkeys;
        ChargeLimitEnabled = _settings.ChargeLimit80Percent;
        LightingBrightness = _settings.Lighting.Brightness;
        LightingSpeed = _settings.Lighting.Speed;
        LightingDirection = _settings.Lighting.Direction;
        LightingPrimaryColor = _settings.Lighting.PrimaryColor;
        LogoLightingEnabled = _settings.Lighting.LogoEnabled;
        SelectedLightingEffect = _settings.Lighting.Effect;

        CpuFanPoints.Clear();
        foreach (FanCurvePoint point in _settings.FanCurve.Cpu)
        {
            CpuFanPoints.Add(new FanCurvePointViewModel(point));
        }

        GpuFanPoints.Clear();
        foreach (FanCurvePoint point in _settings.FanCurve.Gpu)
        {
            GpuFanPoints.Add(new FanCurvePointViewModel(point));
        }

        LightingZones.Clear();
        for (int index = 0; index < 4; index++)
        {
            string color = index < _settings.Lighting.ZoneColors.Count
                ? _settings.Lighting.ZoneColors[index]
                : _settings.Lighting.PrimaryColor;
            LightingZones.Add(new LightingZoneViewModel(index + 1, color));
        }

        RebuildLightingEffects();
    }

    private void ApplyCapabilities(DeviceCapabilities capabilities)
    {
        DeviceModel = capabilities.Device.Model;
        BiosVersion = capabilities.Device.BiosVersion;
        CompatibilityMessage = capabilities.CompatibilityMessage;
        HardwareWritesEnabled = capabilities.CanWriteHardware;
        FanControlAvailable = capabilities.FanControlAvailable && capabilities.CanWriteHardware;
        GpuMuxAvailable = capabilities.GpuMuxAvailable && capabilities.CanWriteHardware;
        LightingAvailable = capabilities.LightingAvailable && capabilities.CanWriteHardware;
        BatteryControlAvailable = capabilities.BatteryControlAvailable && capabilities.CanWriteHardware;
        AcerServiceAvailable = capabilities.AcerServiceAvailable;
        AcerWmiAvailable = capabilities.AcerWmiAvailable;
        ChargeLimitEnabled = capabilities.ChargeLimitEnabled ?? _settings.ChargeLimit80Percent;
        OverdriveEnabled = capabilities.DeviceSettings.TryGetValue(
                DeviceSettingId.LcdOverdrive,
                out DeviceSettingState? overdrive) &&
            overdrive.Enabled == true;

        RefreshRates.Clear();
        foreach (int rate in capabilities.RefreshRates)
        {
            RefreshRates.Add(rate);
        }

        DisplayControlAvailable = capabilities.CanWriteHardware && RefreshRates.Count > 0;
    }

    private void ApplySnapshot(HardwareSnapshot snapshot)
    {
        _snapshot = snapshot;
        CpuTemperatureText = FormatNumber(snapshot.CpuTemperatureC, " °C", 0);
        GpuTemperatureText = FormatNumber(snapshot.GpuTemperatureC, " °C", 0);
        CpuLoadText = FormatNumber(snapshot.CpuLoadPercent, " %", 0);
        GpuLoadText = FormatNumber(snapshot.GpuLoadPercent, " %", 0);
        CpuFanText = FormatNumber(snapshot.CpuFanRpm, " RPM", 0);
        GpuFanText = FormatNumber(snapshot.GpuFanRpm, " RPM", 0);
        CpuPowerText = FormatNumber(snapshot.CpuPowerWatts, " W", 1);
        GpuPowerText = FormatNumber(snapshot.GpuPowerWatts, " W", 1);
        CpuClockText = FormatNumber(snapshot.CpuClockMhz, " MHz", 0);
        GpuClockText = FormatNumber(snapshot.GpuClockMhz, " MHz", 0);
        MemoryText = FormatPair(snapshot.MemoryUsedGb, snapshot.MemoryTotalGb, " GB");
        VramText = FormatPair(snapshot.VramUsedGb, snapshot.VramTotalGb, " GB");
        BatteryText = FormatNumber(snapshot.BatteryPercent, " %", 0);
        PowerSourceText = snapshot.IsOnAcPower switch
        {
            true => _localization.Get("Power.Ac"),
            false => _localization.Get("Power.Battery"),
            null => "--"
        };
        RefreshRateText = FormatNumber(snapshot.DisplayRefreshRate, " Hz", 0);
        FpsText = FormatNumber(_fpsSource.FramesPerSecond, " FPS", 0);

        if (snapshot.OperatingMode is OperatingMode operatingMode)
        {
            CurrentOperatingMode = operatingMode;
            CurrentOperatingModeName = LocalizeMode(operatingMode);
        }

        if (snapshot.FanMode is FanMode fanMode)
        {
            CurrentFanMode = fanMode;
            CurrentFanModeName = LocalizeFanMode(fanMode);
        }

        if (snapshot.GpuMuxMode is GpuMuxMode muxMode)
        {
            CurrentGpuMuxMode = muxMode;
            CurrentGpuMuxModeName = LocalizeMuxMode(muxMode);
        }
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                HardwareSnapshot snapshot = await _platform.ReadSnapshotAsync(cancellationToken);
                ApplySnapshot(snapshot);
                await HandlePowerTransitionAsync(snapshot, cancellationToken);
                if (_customFanActive)
                {
                    await UpdateCustomFanAsync(snapshot, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.Error("Telemetry refresh failed", exception);
            }
        }
    }

    private async Task HandlePowerTransitionAsync(HardwareSnapshot snapshot, CancellationToken cancellationToken)
    {
        bool? previous = _lastAcState;
        _lastAcState = snapshot.IsOnAcPower;
        if (!previous.HasValue || !snapshot.IsOnAcPower.HasValue || previous == snapshot.IsOnAcPower)
        {
            return;
        }

        if (snapshot.IsOnAcPower == false)
        {
            if (AutoEcoOnBattery)
            {
                if (snapshot.OperatingMode is OperatingMode mode && mode != OperatingMode.Eco)
                {
                    _settings.LastAcMode = mode;
                }

                await ApplyAutomaticOperatingModeAsync(OperatingMode.Eco, cancellationToken);
            }

            if (AutoRefreshRate && RefreshRates.Count > 0)
            {
                await ApplyAutomaticRefreshRateAsync(RefreshRates[0], cancellationToken);
            }
        }
        else
        {
            if (AutoEcoOnBattery)
            {
                await ApplyAutomaticOperatingModeAsync(_settings.LastAcMode, cancellationToken);
            }

            if (AutoRefreshRate && RefreshRates.Count > 0)
            {
                await ApplyAutomaticRefreshRateAsync(RefreshRates[^1], cancellationToken);
            }
        }
    }

    private async Task ApplyAutomaticOperatingModeAsync(OperatingMode mode, CancellationToken cancellationToken)
    {
        await _hardwareGate.WaitAsync(cancellationToken);
        try
        {
            if (mode is OperatingMode.Silent or OperatingMode.Eco && _customFanActive)
            {
                ApplyResult autoResult = await _platform.SetFanModeAsync(
                    FanMode.Auto,
                    cancellationToken: cancellationToken);
                if (!autoResult.IsSuccess)
                {
                    PublishResult(autoResult);
                    return;
                }

                await _fanGuard.StopAsync();
                _customFanActive = false;
                _activeFanCurve = null;
                FanGuardActive = false;
                UpdateExtendedTelemetryState();
            }

            ApplyResult result = await _platform.SetOperatingModeAsync(mode, cancellationToken);
            if (result.IsSuccess)
            {
                CurrentOperatingMode = mode;
                CurrentOperatingModeName = LocalizeMode(mode);
                PublishResult(result);
            }
        }
        finally
        {
            _hardwareGate.Release();
        }
    }

    private async Task ApplyAutomaticRefreshRateAsync(int rate, CancellationToken cancellationToken)
    {
        await _hardwareGate.WaitAsync(cancellationToken);
        try
        {
            ApplyResult result = await _platform.SetRefreshRateAsync(rate, OverdriveEnabled, cancellationToken);
            if (result.IsSuccess)
            {
                SelectedRefreshRate = rate;
                RefreshRateText = $"{rate} Hz";
            }
        }
        finally
        {
            _hardwareGate.Release();
        }
    }

    private async Task UpdateCustomFanAsync(HardwareSnapshot snapshot, CancellationToken cancellationToken)
    {
        FanCurve curve = _activeFanCurve ?? BuildFanCurve();
        (int cpu, int gpu) = EvaluateFanTargets(curve, snapshot);
        bool targetChanged = Math.Abs(cpu - _lastCpuFanTarget) >= 2 || Math.Abs(gpu - _lastGpuFanTarget) >= 2;
        bool refreshDue = DateTimeOffset.UtcNow - _lastFanWrite >= TimeSpan.FromSeconds(10);
        if (!targetChanged && !refreshDue)
        {
            return;
        }

        await _hardwareGate.WaitAsync(cancellationToken);
        try
        {
            ApplyResult result = await _platform.SetFanModeAsync(FanMode.Custom, cpu, gpu, cancellationToken);
            if (!result.IsSuccess)
            {
                _customFanActive = false;
                _activeFanCurve = null;
                FanGuardActive = false;
                await _fanGuard.StopAsync();
                UpdateExtendedTelemetryState();
                PublishResult(result);
                return;
            }

            _lastCpuFanTarget = cpu;
            _lastGpuFanTarget = gpu;
            _lastFanWrite = DateTimeOffset.UtcNow;
        }
        finally
        {
            _hardwareGate.Release();
        }
    }

    private async Task RefreshServicesCoreAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedServiceInfo> services = await _platform.GetManagedServicesAsync(cancellationToken);
        ManagedServices.Clear();
        foreach (ManagedServiceInfo service in services)
        {
            ManagedServices.Add(service);
        }
    }

    private void RebuildLocalizedValues()
    {
        CurrentOperatingModeName = CurrentOperatingMode is OperatingMode operating
            ? LocalizeMode(operating)
            : "--";
        CurrentFanModeName = CurrentFanMode is FanMode fan ? LocalizeFanMode(fan) : "--";
        CurrentGpuMuxModeName = CurrentGpuMuxMode is GpuMuxMode mux ? LocalizeMuxMode(mux) : "--";
        PowerSourceText = _snapshot.IsOnAcPower switch
        {
            true => _localization.Get("Power.Ac"),
            false => _localization.Get("Power.Battery"),
            null => "--"
        };
        RebuildLightingEffects();
        RebuildDeviceSettings();
    }

    private void RebuildLightingEffects()
    {
        LightingEffects.Clear();
        foreach (LightingEffect effect in Enum.GetValues<LightingEffect>())
        {
            LightingEffects.Add(new SelectionOption<LightingEffect>(
                effect,
                _localization.Get($"LightingEffect.{effect}")));
        }
    }

    private void RebuildDeviceSettings()
    {
        DeviceSettings.Clear();
        foreach (DeviceSettingState state in _deviceSettingStates.Values.OrderBy(state => state.Id))
        {
            DeviceSettings.Add(new DeviceSettingItemViewModel(
                state,
                _localization.Get($"DeviceSetting.{state.Id}"),
                LocalizeDeviceSettingDetail(state)));
        }
    }

    private string LocalizeDeviceSettingDetail(DeviceSettingState state)
    {
        if (state.IsSupported)
        {
            return state.IsWritable
                ? _localization.Get("DeviceSetting.Available")
                : _localization.Get("DeviceSetting.ReadOnly");
        }

        string detail = state.Detail ?? string.Empty;
        if (detail.Contains("panel", StringComparison.OrdinalIgnoreCase))
        {
            return _localization.Get("DeviceSetting.PanelUnavailable");
        }

        if (detail.Contains("AcerService unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return _localization.Get("DeviceSetting.ServiceUnavailable");
        }

        if (detail.Contains("query", StringComparison.OrdinalIgnoreCase))
        {
            return _localization.Get("DeviceSetting.QueryFailed");
        }

        if (detail.Contains("WMI", StringComparison.OrdinalIgnoreCase))
        {
            return _localization.Get("DeviceSetting.WmiUnavailable");
        }

        return _localization.Get("DeviceSetting.Unsupported");
    }

    private FanCurve BuildFanCurve() => new()
    {
        Cpu = CpuFanPoints.Select(point => point.ToModel()).ToList(),
        Gpu = GpuFanPoints.Select(point => point.ToModel()).ToList(),
        MinimumSpeedPercent = Math.Clamp(_settings.FanCurve.MinimumSpeedPercent, 20, 100)
    };

    private LightingProfile BuildLightingProfile() => new()
    {
        Effect = SelectedLightingEffect,
        Brightness = LightingBrightness,
        Speed = LightingSpeed,
        Direction = LightingDirection,
        PrimaryColor = LightingPrimaryColor,
        ZoneColors = LightingZones.Select(zone => zone.Color).ToList(),
        LogoEnabled = LogoLightingEnabled
    };

    private static (int Cpu, int Gpu) EvaluateFanTargets(FanCurve curve, HardwareSnapshot snapshot)
    {
        double cpuTemperature = snapshot.CpuTemperatureC ?? FanCurveEngine.SafetyTemperatureC;
        double gpuTemperature = snapshot.GpuTemperatureC ?? FanCurveEngine.SafetyTemperatureC;
        return (
            FanCurveEngine.Evaluate(curve.Cpu, cpuTemperature, curve.MinimumSpeedPercent),
            FanCurveEngine.Evaluate(curve.Gpu, gpuTemperature, curve.MinimumSpeedPercent));
    }

    private async Task SaveSettingsAsync()
    {
        _settings.Language = CurrentLanguage;
        _settings.RunAtStartup = RunAtStartup;
        _settings.StartMinimized = StartMinimized;
        _settings.AutoEcoOnBattery = AutoEcoOnBattery;
        _settings.AutoRefreshRate = AutoRefreshRate;
        _settings.ShowOsd = ShowOsd;
        _settings.ShowFps = ShowFps;
        _settings.EnableGlobalHotkeys = EnableGlobalHotkeys;
        _settings.ChargeLimit80Percent = ChargeLimitEnabled;
        await _settingsStore.SaveAsync(_settings, CancellationToken.None);
    }

    private void PublishResult(ApplyResult result)
    {
        StatusIsError = !result.IsSuccess;
        StatusMessage = result.IsSuccess ? _localization.Get("Status.Applied") : result.Message;
        RebootRequired |= result.RequiresReboot;
    }

    private void PublishError(string message)
    {
        StatusIsError = true;
        StatusMessage = message;
    }

    private string LocalizeMode(OperatingMode mode) => _localization.Get($"OperatingMode.{mode}");

    private string LocalizeFanMode(FanMode mode) => _localization.Get($"FanMode.{mode}");

    private string LocalizeMuxMode(GpuMuxMode mode) => _localization.Get($"GpuMuxMode.{mode}");

    private static string FormatNumber(double? value, string suffix, int decimals) => value.HasValue
        ? $"{Math.Round(value.Value, decimals).ToString($"F{decimals}", CultureInfo.CurrentCulture)}{suffix}"
        : "--";

    private static string FormatNumber(int? value, string suffix, int decimals) =>
        FormatNumber(value.HasValue ? (double?)value.Value : null, suffix, decimals);

    private static string FormatPair(double? used, double? total, string suffix) =>
        used.HasValue && total.HasValue
            ? $"{used.Value:F1} / {total.Value:F1}{suffix}"
            : "--";

    private async Task<bool> StopFpsAsync()
    {
        await _fpsSource.StopAsync();
        FpsText = "--";
        return true;
    }

    private void UpdateExtendedTelemetryState() =>
        _platform.SetExtendedTelemetryEnabled(
            SelectedTabIndex == 2 || ShowOsd || ShowFps || _customFanActive);

    partial void OnSelectedTabIndexChanged(int value) => UpdateExtendedTelemetryState();

    partial void OnShowOsdChanged(bool value) => UpdateExtendedTelemetryState();

    partial void OnShowFpsChanged(bool value) => UpdateExtendedTelemetryState();

    private void OnModeKeyPressed(object? sender, EventArgs e)
    {
        _uiDispatcher.Post(CycleOperatingModeCoreAsync);
    }
}
