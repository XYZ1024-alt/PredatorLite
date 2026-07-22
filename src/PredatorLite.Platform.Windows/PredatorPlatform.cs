using System.Text.Json.Nodes;
using PredatorLite.Core.Abstractions;
using PredatorLite.Core.Models;
using PredatorLite.Platform.Windows.Acer;
using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.Platform.Windows;

public sealed class PredatorPlatform : IPredatorPlatform
{
    private readonly AcerServiceClient _service;
    private readonly AcerWmiClient _wmi;
    private readonly DisplayController _display = new();
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    private DeviceCapabilities? _capabilities;
    private OperatingMode? _operatingMode;
    private GpuMuxMode? _gpuMuxMode;
    private FanMode? _fanMode;
    private bool _fanWriteOwned;
    private DateTimeOffset _lastServiceStateRead = DateTimeOffset.MinValue;
    private HardwareMonitorReader? _hardwareMonitor;
    private bool _extendedTelemetryEnabled;

    public PredatorPlatform(IAppLogger logger)
    {
        _logger = logger;
        _service = new AcerServiceClient(logger);
        _wmi = new AcerWmiClient(logger);
    }

    public async Task<DeviceCapabilities> ProbeAsync(CancellationToken cancellationToken = default)
    {
        DeviceIdentity identity = await Task.Run(SystemIdentityReader.Read, cancellationToken).ConfigureAwait(false);
        bool serviceAvailable = await _service.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
        bool wmiAvailable = await _wmi.IsGamingInterfaceAvailableAsync(cancellationToken).ConfigureAwait(false);
        bool batteryAvailable = await _wmi.IsBatteryInterfaceAvailableAsync(cancellationToken).ConfigureAwait(false);
        bool? chargeLimitEnabled = batteryAvailable
            ? await _wmi.ReadChargeLimitAsync(cancellationToken).ConfigureAwait(false)
            : null;
        IReadOnlyList<int> refreshRates = await Task.Run(_display.GetSupportedRefreshRates, cancellationToken)
            .ConfigureAwait(false);

        bool modelMatches = identity.Manufacturer.Contains("Acer", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(identity.Model, "Predator PHN16-71", StringComparison.OrdinalIgnoreCase);
        bool biosMatches = string.Equals(identity.BiosVersion, "V1.20", StringComparison.OrdinalIgnoreCase);
        bool validated = modelMatches && biosMatches;

        AcerResponse? fanState = serviceAvailable
            ? await TryQueryAsync(AcerProtocol.FanControl, cancellationToken).ConfigureAwait(false)
            : null;
        AcerResponse? lightingState = serviceAvailable
            ? await TryQueryAsync(AcerProtocol.Lighting, cancellationToken).ConfigureAwait(false)
            : null;
        AcerResponse? gpuState = serviceAvailable
            ? await TryQueryAsync(AcerProtocol.GpuMode, cancellationToken).ConfigureAwait(false)
            : null;

        await RefreshServiceStateAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<DeviceSettingId, DeviceSettingState> settings =
            await QueryDeviceSettingsCoreAsync(serviceAvailable, validated, cancellationToken).ConfigureAwait(false);
        _capabilities = new DeviceCapabilities
        {
            Device = identity,
            IsValidatedModel = validated,
            CompatibilityMessage = validated
                ? "Predator PHN16-71 BIOS V1.20 validated."
                : modelMatches
                    ? $"BIOS {identity.BiosVersion} is not yet validated; hardware writes are disabled."
                    : $"Model {identity.Model} is not yet supported; diagnostics remain available.",
            AcerServiceAvailable = serviceAvailable,
            AcerWmiAvailable = wmiAvailable,
            BatteryControlAvailable = batteryAvailable,
            ChargeLimitEnabled = chargeLimitEnabled,
            LightingAvailable = lightingState?.IsSuccess == true,
            FanControlAvailable = fanState?.IsSuccess == true || wmiAvailable,
            GpuMuxAvailable = gpuState?.IsSuccess == true,
            RefreshRates = refreshRates,
            DeviceSettings = settings
        };

        _logger.Info($"Capability probe: {identity.Model}, BIOS {identity.BiosVersion}, " +
            $"AcerService={serviceAvailable}, WMI={wmiAvailable}, validated={validated}.");
        return _capabilities;
    }

    public async Task<HardwareSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        bool useWmiSensors = _capabilities?.AcerWmiAvailable == true;
        Task<int?> cpuTemperatureTask = useWmiSensors
            ? _wmi.ReadSensorAsync(AcerProtocol.CpuTemperatureSensor, cancellationToken)
            : Task.FromResult<int?>(null);
        Task<int?> gpuTemperatureTask = useWmiSensors
            ? _wmi.ReadSensorAsync(AcerProtocol.GpuTemperatureSensor, cancellationToken)
            : Task.FromResult<int?>(null);
        Task<int?> cpuFanTask = useWmiSensors
            ? _wmi.ReadSensorAsync(AcerProtocol.CpuFanRpmSensor, cancellationToken)
            : Task.FromResult<int?>(null);
        Task<int?> gpuFanTask = useWmiSensors
            ? _wmi.ReadSensorAsync(AcerProtocol.GpuFanRpmSensor, cancellationToken)
            : Task.FromResult<int?>(null);
        Task<ExtraTelemetry> extraTask = _extendedTelemetryEnabled && _hardwareMonitor is not null
            ? Task.Run(_hardwareMonitor.Read, cancellationToken)
            : Task.FromResult(new ExtraTelemetry());

        if (DateTimeOffset.UtcNow - _lastServiceStateRead > TimeSpan.FromSeconds(10))
        {
            await RefreshServiceStateAsync(cancellationToken).ConfigureAwait(false);
        }

        ExtraTelemetry extra = await extraTask.ConfigureAwait(false);
        int? cpuTemperature = NormalizeTemperature(await cpuTemperatureTask.ConfigureAwait(false)) ??
            NormalizeTemperature(extra.CpuTemperatureC);
        int? gpuTemperature = NormalizeTemperature(await gpuTemperatureTask.ConfigureAwait(false)) ??
            NormalizeTemperature(extra.GpuTemperatureC);
        (bool? onAc, int? batteryPercent) = PowerStatusReader.Read();

        return new HardwareSnapshot
        {
            CpuTemperatureC = cpuTemperature,
            GpuTemperatureC = gpuTemperature,
            CpuFanRpm = NormalizeRpm(await cpuFanTask.ConfigureAwait(false)),
            GpuFanRpm = NormalizeRpm(await gpuFanTask.ConfigureAwait(false)),
            CpuLoadPercent = extra.CpuLoadPercent,
            GpuLoadPercent = extra.GpuLoadPercent,
            CpuPowerWatts = extra.CpuPowerWatts,
            GpuPowerWatts = extra.GpuPowerWatts,
            CpuClockMhz = extra.CpuClockMhz,
            GpuClockMhz = extra.GpuClockMhz,
            MemoryUsedGb = extra.MemoryUsedGb,
            MemoryTotalGb = extra.MemoryTotalGb,
            VramUsedGb = extra.VramUsedGb,
            VramTotalGb = extra.VramTotalGb,
            BatteryPercent = batteryPercent,
            IsOnAcPower = onAc,
            DisplayRefreshRate = _display.GetCurrentRefreshRate(),
            OperatingMode = _operatingMode,
            GpuMuxMode = _gpuMuxMode,
            FanMode = _fanMode
        };
    }

    public void SetExtendedTelemetryEnabled(bool enabled)
    {
        if (_extendedTelemetryEnabled == enabled)
        {
            return;
        }

        _extendedTelemetryEnabled = enabled;
        if (enabled)
        {
            _hardwareMonitor = new HardwareMonitorReader();
        }
        else
        {
            _hardwareMonitor?.Dispose();
            _hardwareMonitor = null;
        }
    }

    public async Task<IReadOnlyDictionary<DeviceSettingId, DeviceSettingState>> ReadDeviceSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        bool serviceAvailable = _capabilities?.AcerServiceAvailable ??
            await _service.IsAvailableAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<DeviceSettingId, DeviceSettingState> states =
            await QueryDeviceSettingsCoreAsync(
                serviceAvailable,
                _capabilities?.IsValidatedModel == true,
                cancellationToken).ConfigureAwait(false);
        if (_capabilities is not null)
        {
            _capabilities = _capabilities with { DeviceSettings = states };
        }

        return states;
    }

    public async Task<ApplyResult> SetOperatingModeAsync(
        OperatingMode mode,
        CancellationToken cancellationToken = default)
    {
        ApplyResult? blocked = EnsureWriteAllowed();
        if (blocked is not null)
        {
            return blocked;
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_capabilities!.AcerServiceAvailable)
            {
                AcerResponse response = await _service.SetAsync(
                    AcerProtocol.OperatingMode,
                    new JsonObject { ["mode"] = (int)mode },
                    cancellationToken).ConfigureAwait(false);
                if (response.IsSuccess && await VerifyIntAsync(
                        AcerProtocol.OperatingMode,
                        "mode",
                        (int)mode,
                        cancellationToken).ConfigureAwait(false))
                {
                    _operatingMode = mode;
                    _wmi.ApplyWindowsPowerOverlay(mode);
                    return ApplyResult.Success($"Operating mode changed to {mode}.");
                }
            }

            if (_capabilities.AcerWmiAvailable &&
                await _wmi.SetOperatingModeAsync(mode, cancellationToken).ConfigureAwait(false))
            {
                OperatingMode? readBack = await _wmi.ReadOperatingModeAsync(cancellationToken).ConfigureAwait(false);
                if (readBack == mode)
                {
                    _operatingMode = mode;
                    _wmi.ApplyWindowsPowerOverlay(mode);
                    return ApplyResult.Success($"Operating mode changed to {mode} through WMI.");
                }
            }

            return ApplyResult.Failure("The operating mode could not be verified.");
        }
        catch (Exception exception)
        {
            _logger.Error($"Operating mode {mode} failed", exception);
            return ApplyResult.Failure(exception.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ApplyResult> SetFanModeAsync(
        FanMode mode,
        int cpuSpeedPercent = 50,
        int gpuSpeedPercent = 50,
        CancellationToken cancellationToken = default)
    {
        ApplyResult? blocked = EnsureWriteAllowed();
        if (blocked is not null)
        {
            return blocked;
        }

        if (mode == FanMode.Custom && _operatingMode is OperatingMode.Silent or OperatingMode.Eco)
        {
            return ApplyResult.Unsupported("Custom fan control is disabled in Silent and Eco modes.");
        }

        int cpu = Math.Clamp(cpuSpeedPercent, 20, 100);
        int gpu = Math.Clamp(gpuSpeedPercent, 20, 100);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_capabilities!.AcerServiceAvailable)
            {
                JsonObject parameters = CreateFanParameters(mode, cpu, gpu);
                AcerResponse response = await _service.SetAsync(
                    AcerProtocol.FanControl,
                    parameters,
                    cancellationToken).ConfigureAwait(false);
                if (response.IsSuccess && await VerifyIntAsync(
                        AcerProtocol.FanControl,
                        "mode",
                        (int)mode,
                        cancellationToken).ConfigureAwait(false))
                {
                    _fanMode = mode;
                    _fanWriteOwned = mode is FanMode.Max or FanMode.Custom;
                    return ApplyResult.Success($"Fan mode changed to {mode}.");
                }
            }

            if (_capabilities.AcerWmiAvailable &&
                await _wmi.SetFanModeAsync(mode, cpu, gpu, cancellationToken).ConfigureAwait(false))
            {
                _fanMode = mode;
                _fanWriteOwned = mode is FanMode.Max or FanMode.Custom;
                return ApplyResult.Success($"Fan mode changed to {mode} through WMI.");
            }

            return ApplyResult.Failure("The fan mode could not be applied.");
        }
        catch (Exception exception)
        {
            _logger.Error($"Fan mode {mode} failed", exception);
            return ApplyResult.Failure(exception.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ApplyResult> SetGpuMuxModeAsync(
        GpuMuxMode mode,
        CancellationToken cancellationToken = default)
    {
        ApplyResult? blocked = EnsureWriteAllowed();
        if (blocked is not null)
        {
            return blocked;
        }

        if (_capabilities?.GpuMuxAvailable != true)
        {
            return ApplyResult.Unsupported("GPU MUX control is not available.");
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            GpuMuxMode? previous = _gpuMuxMode;
            AcerResponse response = await _service.SetAsync(
                AcerProtocol.GpuMode,
                new JsonObject { ["mode"] = (int)mode },
                cancellationToken).ConfigureAwait(false);
            bool verified = response.IsSuccess && await VerifyIntAsync(
                AcerProtocol.GpuMode,
                "mode",
                (int)mode,
                cancellationToken).ConfigureAwait(false);
            if (!verified)
            {
                return ApplyResult.Failure("The GPU routing mode could not be verified.");
            }

            _gpuMuxMode = mode;
            bool changed = !previous.HasValue || previous.Value != mode;
            return ApplyResult.Success(
                mode == GpuMuxMode.Hybrid
                    ? "Hybrid graphics mode selected."
                    : "Discrete GPU mode selected.",
                requiresReboot: changed);
        }
        catch (Exception exception)
        {
            _logger.Error($"GPU MUX mode {mode} failed", exception);
            return ApplyResult.Failure(exception.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ApplyResult> SetChargeLimitAsync(
        bool limitTo80Percent,
        CancellationToken cancellationToken = default)
    {
        ApplyResult? blocked = EnsureWriteAllowed();
        if (blocked is not null)
        {
            return blocked;
        }

        if (_capabilities?.BatteryControlAvailable != true)
        {
            return ApplyResult.Unsupported("Battery charge limiting is not available.");
        }

        bool applied = await _wmi.SetChargeLimitAsync(limitTo80Percent, cancellationToken).ConfigureAwait(false);
        bool? readBack = applied
            ? await _wmi.ReadChargeLimitAsync(cancellationToken).ConfigureAwait(false)
            : null;
        return readBack == limitTo80Percent
            ? ApplyResult.Success(limitTo80Percent ? "Battery charge limit set to 80%." : "Battery charge limit set to 100%.")
            : ApplyResult.Failure("The battery charge limit could not be verified.");
    }

    public async Task<ApplyResult> SetLightingAsync(
        LightingProfile profile,
        CancellationToken cancellationToken = default)
    {
        ApplyResult? blocked = EnsureWriteAllowed();
        if (blocked is not null)
        {
            return blocked;
        }

        if (_capabilities?.LightingAvailable != true)
        {
            return ApplyResult.Unsupported("Keyboard lighting is not available.");
        }

        try
        {
            AcerResponse keyboard = await _service.SetAsync(
                AcerProtocol.Lighting,
                LightingPayloadFactory.CreateKeyboardPayload(profile),
                cancellationToken).ConfigureAwait(false);
            if (!keyboard.IsSuccess)
            {
                return ApplyResult.Failure("Keyboard lighting was rejected by AcerService.");
            }

            AcerResponse logo = await _service.SetAsync(
                AcerProtocol.Lighting,
                LightingPayloadFactory.CreateLogoPayload(profile),
                cancellationToken).ConfigureAwait(false);
            return ApplyResult.Success(logo.IsSuccess
                ? "Keyboard and logo lighting applied."
                : "Keyboard lighting applied; logo lighting is unavailable.");
        }
        catch (Exception exception)
        {
            _logger.Error("Lighting update failed", exception);
            return ApplyResult.Failure(exception.Message);
        }
    }

    public async Task<ApplyResult> SetDeviceSettingAsync(
        DeviceSettingId setting,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ApplyResult? blocked = EnsureWriteAllowed();
        if (blocked is not null)
        {
            return blocked;
        }

        IReadOnlyDictionary<DeviceSettingId, DeviceSettingState> states =
            await ReadDeviceSettingsAsync(cancellationToken).ConfigureAwait(false);
        if (!states.TryGetValue(setting, out DeviceSettingState? state) || !state.IsSupported || !state.IsWritable)
        {
            return ApplyResult.Unsupported($"{setting} is not writable on this device.");
        }

        if (setting == DeviceSettingId.KeyboardBacklightTimeout)
        {
            bool applied = await _wmi.SetKeyboardTimeoutAsync(enabled, cancellationToken).ConfigureAwait(false);
            bool? readBack = applied
                ? await _wmi.ReadKeyboardTimeoutAsync(cancellationToken).ConfigureAwait(false)
                : null;
            return readBack == enabled
                ? ApplyResult.Success($"{setting} changed.")
                : ApplyResult.Failure($"{setting} could not be verified.");
        }

        string? function = ToServiceFunction(setting);
        if (function is null)
        {
            return ApplyResult.Unsupported($"{setting} has no service command.");
        }

        string property = setting is DeviceSettingId.SoundMode or DeviceSettingId.PanelDynamicRefresh
            ? "mode"
            : "status";
        try
        {
            AcerResponse response = await _service.SetAsync(
                function,
                new JsonObject { [property] = enabled ? 1 : 0 },
                cancellationToken).ConfigureAwait(false);
            if (response.IsSuccess && await VerifyIntAsync(
                    function,
                    property,
                    enabled ? 1 : 0,
                    cancellationToken).ConfigureAwait(false))
            {
                return ApplyResult.Success($"{setting} changed.");
            }

            if (setting == DeviceSettingId.LcdOverdrive &&
                await _wmi.SetLcdOverdriveAsync(enabled, cancellationToken).ConfigureAwait(false))
            {
                return ApplyResult.Success("LCD overdrive changed through WMI.");
            }

            return ApplyResult.Failure($"{setting} could not be verified.");
        }
        catch (Exception exception)
        {
            _logger.Error($"Device setting {setting} failed", exception);
            return ApplyResult.Failure(exception.Message);
        }
    }

    public async Task<ApplyResult> SetRefreshRateAsync(
        int refreshRate,
        bool enableOverdrive,
        CancellationToken cancellationToken = default)
    {
        ApplyResult? blocked = EnsureWriteAllowed();
        if (blocked is not null)
        {
            return blocked;
        }

        bool changed = await Task.Run(() => _display.SetRefreshRate(refreshRate), cancellationToken)
            .ConfigureAwait(false);
        if (!changed || _display.GetCurrentRefreshRate() != refreshRate)
        {
            return ApplyResult.Failure($"Refresh rate {refreshRate} Hz could not be applied.");
        }

        if (_capabilities?.DeviceSettings.TryGetValue(DeviceSettingId.LcdOverdrive, out DeviceSettingState? overdrive) == true &&
            overdrive.IsSupported)
        {
            ApplyResult overdriveResult = await SetDeviceSettingAsync(
                DeviceSettingId.LcdOverdrive,
                enableOverdrive,
                cancellationToken).ConfigureAwait(false);
            if (!overdriveResult.IsSuccess)
            {
                return ApplyResult.Failure($"Refresh rate changed, but overdrive failed: {overdriveResult.Message}");
            }
        }

        return ApplyResult.Success($"Display refresh rate changed to {refreshRate} Hz.");
    }

    public Task<IReadOnlyList<ManagedServiceInfo>> GetManagedServicesAsync(
        CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<ManagedServiceInfo>>(ServiceInspector.Read, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_fanWriteOwned && _fanMode is FanMode.Max or FanMode.Custom)
            {
                await SetFanModeCoreWithoutValidationAsync(FanMode.Auto, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch
        {
        }

        _hardwareMonitor?.Dispose();
        await _service.DisposeAsync().ConfigureAwait(false);
        _operationGate.Dispose();
    }

    private async Task RefreshServiceStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            AcerResponse operating = await _service.QueryAsync(AcerProtocol.OperatingMode, cancellationToken)
                .ConfigureAwait(false);
            if (operating.IsSuccess && operating.TryGetInt("mode", out int operatingMode) &&
                Enum.IsDefined(typeof(OperatingMode), (byte)operatingMode))
            {
                _operatingMode = (OperatingMode)(byte)operatingMode;
            }

            AcerResponse fan = await _service.QueryAsync(AcerProtocol.FanControl, cancellationToken).ConfigureAwait(false);
            if (fan.IsSuccess && fan.TryGetInt("mode", out int fanMode) && Enum.IsDefined(typeof(FanMode), fanMode))
            {
                _fanMode = (FanMode)fanMode;
            }

            AcerResponse gpu = await _service.QueryAsync(AcerProtocol.GpuMode, cancellationToken).ConfigureAwait(false);
            if (gpu.IsSuccess && gpu.TryGetInt("mode", out int gpuMode) && Enum.IsDefined(typeof(GpuMuxMode), gpuMode))
            {
                _gpuMuxMode = (GpuMuxMode)gpuMode;
            }

            _lastServiceStateRead = DateTimeOffset.UtcNow;
        }
        catch (Exception exception)
        {
            _logger.Error("AcerService state refresh failed", exception);
        }
    }

    private async Task<Dictionary<DeviceSettingId, DeviceSettingState>> QueryDeviceSettingsCoreAsync(
        bool serviceAvailable,
        bool isValidatedPhn1671,
        CancellationToken cancellationToken)
    {
        Dictionary<DeviceSettingId, DeviceSettingState> states = [];
        foreach ((DeviceSettingId id, string function, bool writable) in new[]
        {
            (DeviceSettingId.WindowsKey, AcerProtocol.WindowsKey, true),
            (DeviceSettingId.StickyKeys, AcerProtocol.StickyKeys, true),
            (DeviceSettingId.BootSound, AcerProtocol.BootSound, true),
            (DeviceSettingId.LcdOverdrive, AcerProtocol.LcdOverdrive, true),
            (DeviceSettingId.PanelDynamicRefresh, AcerProtocol.PanelDfrMode, true),
            (DeviceSettingId.SoundMode, AcerProtocol.SoundMode, true),
            (DeviceSettingId.BatteryBoost, AcerProtocol.BatteryBoost, false)
        })
        {
            if (isValidatedPhn1671 && id is DeviceSettingId.PanelDynamicRefresh or DeviceSettingId.SoundMode)
            {
                states[id] = new DeviceSettingState(
                    id,
                    false,
                    false,
                    null,
                    id == DeviceSettingId.SoundMode
                        ? "Not supported by PHN16-71/V1.20"
                        : "Unavailable on this panel");
                continue;
            }

            if (!serviceAvailable)
            {
                states[id] = new DeviceSettingState(id, false, false, null, "AcerService unavailable");
                continue;
            }

            AcerResponse? response = await TryQueryAsync(function, cancellationToken).ConfigureAwait(false);
            string property = id is DeviceSettingId.SoundMode or DeviceSettingId.PanelDynamicRefresh ? "mode" : "status";
            int value = 0;
            bool supported = response?.IsSuccess == true && response.TryGetInt(property, out value);
            states[id] = new DeviceSettingState(
                id,
                supported,
                supported && writable,
                supported ? value != 0 : null,
                response is null ? "Query failed" : response.Result switch
                {
                    2 => "Not supported by AcerService",
                    3 => "Unavailable on this panel",
                    0 => null,
                    _ => $"AcerService result {response.Result}"
                });
        }

        bool? ledTimeout = await _wmi.ReadKeyboardTimeoutAsync(cancellationToken).ConfigureAwait(false);
        states[DeviceSettingId.KeyboardBacklightTimeout] = new DeviceSettingState(
            DeviceSettingId.KeyboardBacklightTimeout,
            ledTimeout.HasValue,
            ledTimeout.HasValue,
            ledTimeout,
            ledTimeout.HasValue ? null : "APGe WMI unavailable");
        return states;
    }

    private async Task<AcerResponse?> TryQueryAsync(string function, CancellationToken cancellationToken)
    {
        try
        {
            return await _service.QueryAsync(function, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Info($"AcerService capability query {function} timed out.");
            return null;
        }
        catch (Exception exception)
        {
            _logger.Error($"AcerService capability query {function} failed", exception);
            return null;
        }
    }

    private async Task<bool> VerifyIntAsync(
        string function,
        string property,
        int expected,
        CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        AcerResponse? response = await TryQueryAsync(function, cancellationToken).ConfigureAwait(false);
        return response?.IsSuccess == true && response.TryGetInt(property, out int value) && value == expected;
    }

    private ApplyResult? EnsureWriteAllowed()
    {
        if (_capabilities is null)
        {
            return ApplyResult.Failure("Hardware capabilities have not been probed.");
        }

        return _capabilities.CanWriteHardware
            ? null
            : ApplyResult.Unsupported(_capabilities.CompatibilityMessage);
    }

    private async Task SetFanModeCoreWithoutValidationAsync(FanMode mode, CancellationToken cancellationToken)
    {
        try
        {
            AcerResponse response = await _service.SetAsync(
                AcerProtocol.FanControl,
                CreateFanParameters(mode, 50, 50),
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                await _wmi.SetFanModeAsync(mode, 50, 50, cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            await _wmi.SetFanModeAsync(mode, 50, 50, cancellationToken).ConfigureAwait(false);
        }
    }

    private static JsonObject CreateFanParameters(FanMode mode, int cpu, int gpu)
    {
        JsonObject parameters = new() { ["mode"] = (int)mode };
        if (mode is FanMode.Auto or FanMode.Custom)
        {
            bool automatic = mode == FanMode.Auto;
            parameters["custom_fan_data"] = new JsonArray
            {
                new JsonObject
                {
                    ["fan_custom_auto"] = automatic ? 1 : 0,
                    ["fan_custom_speed"] = cpu,
                    ["fan_name"] = "CPU"
                },
                new JsonObject
                {
                    ["fan_custom_auto"] = automatic ? 1 : 0,
                    ["fan_custom_speed"] = gpu,
                    ["fan_name"] = "GPU"
                }
            };
        }

        return parameters;
    }

    private static string? ToServiceFunction(DeviceSettingId setting) => setting switch
    {
        DeviceSettingId.WindowsKey => AcerProtocol.WindowsKey,
        DeviceSettingId.StickyKeys => AcerProtocol.StickyKeys,
        DeviceSettingId.BootSound => AcerProtocol.BootSound,
        DeviceSettingId.LcdOverdrive => AcerProtocol.LcdOverdrive,
        DeviceSettingId.PanelDynamicRefresh => AcerProtocol.PanelDfrMode,
        DeviceSettingId.SoundMode => AcerProtocol.SoundMode,
        _ => null
    };

    private static int? NormalizeTemperature(int? value) => value is > 0 and < 130 ? value : null;

    private static int? NormalizeTemperature(double? value) => value is > 0 and < 130 ? (int)Math.Round(value.Value) : null;

    private static int? NormalizeRpm(int? value) => value is > 0 and < 20000
        ? (int)(Math.Round(value.Value / 100d) * 100)
        : null;
}
