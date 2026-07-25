using System.Diagnostics;
using System.Text.Json.Nodes;
using PredatorLite.Core.Abstractions;
using PredatorLite.Core.Models;
using PredatorLite.Platform.Windows.Acer;
using PredatorLite.Platform.Windows.SystemIntegration;

namespace PredatorLite.Platform.Windows;

public sealed class PredatorPlatform : IPredatorPlatform
{
    private static readonly TimeSpan StartupBackendRetryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StartupBackendInitialDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StartupBackendMaximumDelay = TimeSpan.FromSeconds(2);

    private readonly AcerServiceClient _service;
    private readonly AcerSystemMonitorClient _systemMonitor;
    private readonly AcerWmiClient _wmi;
    private readonly IAppLogger _logger;
    private readonly IFanGuardOwnership? _fanGuardOwnership;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private readonly WindowsCpuTelemetryReader _windowsCpuTelemetry;

    private DeviceCapabilities? _capabilities;
    private PlatformStartupState? _startupState;
    private AcerMonitorTelemetry? _initialAcerTelemetry;
    private OperatingMode? _operatingMode;
    private OperatingMode? _startupObservedOperatingMode;
    private bool _startupObservationAvailable;
    private GpuMuxMode? _gpuMuxMode;
    private FanMode? _fanMode;
    private bool _fanWriteOwned;
    private DateTimeOffset _lastServiceStateRead = DateTimeOffset.MinValue;
    private HardwareMonitorReader? _hardwareMonitor;
    private bool _extendedTelemetryEnabled;
    private bool _fullProbeCompleted;

    public PredatorPlatform(
        IAppLogger logger,
        IFanGuardOwnership? fanGuardOwnership = null)
    {
        _logger = logger;
        _fanGuardOwnership = fanGuardOwnership;
        _service = new AcerServiceClient(logger);
        _systemMonitor = new AcerSystemMonitorClient(logger);
        _wmi = new AcerWmiClient(logger);
        _windowsCpuTelemetry = new WindowsCpuTelemetryReader(logger);
    }

    public async Task<PlatformStartupState> ProbeStartupAsync(CancellationToken cancellationToken = default)
    {
        await _probeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_startupState is not null)
            {
                return _startupState;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Task<DeviceIdentity> identityTask = Task.Run(SystemIdentityReader.Read, cancellationToken);
            Task<AcerResponse?> serviceModeTask = TryQueryAsync(
                AcerProtocol.OperatingMode,
                cancellationToken);
            Task<OperatingMode?> wmiModeTask = _wmi.ReadOperatingModeAsync(cancellationToken);
            (bool? onAcPower, int? batteryPercent) = PowerStatusReader.Read();

            await Task.WhenAll(identityTask, serviceModeTask, wmiModeTask).ConfigureAwait(false);
            DeviceIdentity identity = await identityTask.ConfigureAwait(false);
            AcerResponse? serviceMode = await serviceModeTask.ConfigureAwait(false);
            OperatingMode? wmiMode = await wmiModeTask.ConfigureAwait(false);
            bool serviceAvailable = serviceMode?.IsSuccess == true;
            bool wmiAvailable = wmiMode.HasValue;

            if (AcerControlStateParser.TryReadOperatingMode(serviceMode, out OperatingMode operatingMode))
            {
                SetVerifiedOperatingMode(operatingMode, allowStartupIdempotence: true);
            }
            else if (!serviceAvailable && wmiMode is OperatingMode fallbackMode)
            {
                SetVerifiedOperatingMode(fallbackMode, allowStartupIdempotence: true);
            }

            bool validated = HasTargetProfile(identity);
            if (validated && !serviceAvailable && !wmiAvailable)
            {
                serviceMode = await RetryStartupServiceModeAsync(stopwatch, cancellationToken)
                    .ConfigureAwait(false);
                serviceAvailable = serviceMode?.IsSuccess == true;
                if (AcerControlStateParser.TryReadOperatingMode(serviceMode, out operatingMode))
                {
                    SetVerifiedOperatingMode(operatingMode, allowStartupIdempotence: true);
                }
            }

            DeviceCapabilities capabilities = CreateCapabilities(
                identity,
                serviceAvailable,
                wmiAvailable);
            _capabilities = capabilities;
            _startupState = new PlatformStartupState(
                capabilities,
                new PowerState(onAcPower, batteryPercent),
                _operatingMode);
            _logger.Info(
                $"Startup control probe completed in {stopwatch.ElapsedMilliseconds} ms: " +
                $"AcerService={serviceAvailable}, WMI={wmiAvailable}, validated={validated}.");
            return _startupState;
        }
        finally
        {
            _probeGate.Release();
        }
    }

    public async Task<DeviceCapabilities> ProbeAsync(CancellationToken cancellationToken = default)
    {
        PlatformStartupState startup = _startupState ??
            await ProbeStartupAsync(cancellationToken).ConfigureAwait(false);

        await _probeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_fullProbeCompleted && _capabilities is not null)
            {
                return _capabilities;
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Task<AcerMonitorTelemetry?> systemMonitorTask = _systemMonitor.ReadAsync(cancellationToken);
            Task<(bool Available, bool? Enabled)> batteryTask = ProbeBatteryAsync(cancellationToken);
            Task<IReadOnlyList<int>> refreshRatesTask = Task.Run(
                DisplayController.GetSupportedRefreshRates,
                cancellationToken);
            Task<ServiceProbeResult> serviceTask = ProbeServiceCapabilitiesAsync(
                startup.Capabilities.AcerServiceAvailable,
                ResolveTargetProfile(startup.Capabilities.Device),
                cancellationToken);

            ServiceProbeResult service = await serviceTask.ConfigureAwait(false);
            AcerMonitorTelemetry? initialTelemetry = await systemMonitorTask.ConfigureAwait(false);
            (bool batteryAvailable, bool? chargeLimitEnabled) = await batteryTask.ConfigureAwait(false);
            IReadOnlyList<int> refreshRates = await refreshRatesTask.ConfigureAwait(false);
            bool serviceAvailable = service.ServiceAvailable;
            bool wmiAvailable = startup.Capabilities.AcerWmiAvailable;
            DeviceIdentity identity = startup.Capabilities.Device;
            HardwareTargetProfile? profile = ResolveTargetProfile(identity);
            HardwareWriteBlockReason writeBlockReason = GetWriteBlockReason(
                identity,
                serviceAvailable,
                wmiAvailable);

            _capabilities = new DeviceCapabilities
            {
                Device = identity,
                TargetProfileId = profile?.Id,
                IsValidatedTarget = profile is not null,
                AuthorizedControls = profile?.AuthorizedControls ?? HardwareControlCapabilities.None,
                WriteBlockReason = writeBlockReason,
                CompatibilityMessage = GetCompatibilityMessage(identity, writeBlockReason),
                AcerServiceAvailable = serviceAvailable,
                AcerWmiAvailable = wmiAvailable,
                AcerSystemMonitorAvailable = initialTelemetry?.HasPrimaryTelemetry == true,
                BatteryControlAvailable = batteryAvailable && HasControl(profile, HardwareControlCapabilities.BatteryHealth),
                ChargeLimitEnabled = chargeLimitEnabled,
                LightingAvailable = HasControl(profile, HardwareControlCapabilities.Lighting) &&
                    service.Lighting?.IsSuccess == true,
                FanControlAvailable = HasControl(profile, HardwareControlCapabilities.FanControl) &&
                    (service.Fan?.IsSuccess == true || wmiAvailable),
                GpuMuxAvailable = HasControl(profile, HardwareControlCapabilities.GpuMux) &&
                    service.Gpu?.IsSuccess == true,
                RefreshRates = refreshRates,
                DeviceSettings = service.DeviceSettings
            };

            _initialAcerTelemetry = initialTelemetry;
            _startupState = startup with
            {
                Capabilities = _capabilities,
                OperatingMode = _operatingMode
            };
            _fullProbeCompleted = true;
            _logger.Info($"Capability probe completed in {stopwatch.ElapsedMilliseconds} ms: " +
                $"{identity.Model}, BIOS {identity.BiosVersion}, AcerService={serviceAvailable}, " +
                $"AcerSystemMonitor={_capabilities.AcerSystemMonitorAvailable}, " +
                $"WMI={wmiAvailable}, validated={_capabilities.IsValidatedTarget}.");
            return _capabilities;
        }
        finally
        {
            _probeGate.Release();
        }
    }

    public async Task<HardwareSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        AcerMonitorTelemetry? initialTelemetry = Interlocked.Exchange(ref _initialAcerTelemetry, null);
        Task<AcerMonitorTelemetry?> acerMonitorTask = initialTelemetry is not null
            ? Task.FromResult<AcerMonitorTelemetry?>(initialTelemetry)
            : _systemMonitor.ReadAsync(cancellationToken);
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

        AcerMonitorTelemetry? acerMonitor = await acerMonitorTask.ConfigureAwait(false);
        Task<WindowsCpuTelemetry> windowsCpuTask =
            acerMonitor?.CpuLoadPercent is null || acerMonitor.CpuClockMhz is null
                ? Task.Run(_windowsCpuTelemetry.Read, cancellationToken)
                : Task.FromResult(new WindowsCpuTelemetry());
        ExtraTelemetry extra = await extraTask.ConfigureAwait(false);
        AcerWmiTelemetry wmiTelemetry = new(
            CpuTemperatureC: NormalizeTemperature(await cpuTemperatureTask.ConfigureAwait(false)),
            GpuTemperatureC: NormalizeTemperature(await gpuTemperatureTask.ConfigureAwait(false)),
            CpuFanRpm: NormalizeRpm(await cpuFanTask.ConfigureAwait(false)),
            GpuFanRpm: NormalizeRpm(await gpuFanTask.ConfigureAwait(false)));
        HardwareSnapshot telemetry = TelemetryMerger.Merge(
            acerMonitor,
            wmiTelemetry,
            extra,
            await windowsCpuTask.ConfigureAwait(false));
        (bool? onAc, int? batteryPercent) = PowerStatusReader.Read();

        return telemetry with
        {
            BatteryPercent = batteryPercent,
            IsOnAcPower = onAc,
            DisplayRefreshRate = DisplayController.GetCurrentRefreshRate(),
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
            _hardwareMonitor = new HardwareMonitorReader(_logger);
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
        HardwareTargetProfile? profile = _capabilities is null
            ? null
            : ResolveTargetProfile(_capabilities.Device);
        Dictionary<DeviceSettingId, DeviceSettingState> states =
            await QueryDeviceSettingsCoreAsync(
                serviceAvailable,
                profile,
                cancellationToken).ConfigureAwait(false);
        if (_capabilities is not null)
        {
            _capabilities = _capabilities with { DeviceSettings = states };
        }

        return states;
    }

    public Task<ApplyResult> EnsureStartupOperatingModeAsync(
        OperatingMode mode,
        CancellationToken cancellationToken = default) =>
        SetOperatingModeCoreAsync(mode, useStartupObservation: true, cancellationToken);

    public Task<ApplyResult> SetOperatingModeAsync(
        OperatingMode mode,
        CancellationToken cancellationToken = default) =>
        SetOperatingModeCoreAsync(mode, useStartupObservation: false, cancellationToken);

    private async Task<ApplyResult> SetOperatingModeCoreAsync(
        OperatingMode mode,
        bool useStartupObservation,
        CancellationToken cancellationToken)
    {
        if (mode is not (
            OperatingMode.Silent or
            OperatingMode.Balanced or
            OperatingMode.Performance or
            OperatingMode.Turbo or
            OperatingMode.Eco))
        {
            return ApplyResult.Failure("The operating mode value is invalid.");
        }

        ApplyResult? blocked = EnsureWriteAllowed(HardwareControlCapabilities.OperatingMode);
        if (blocked is not null)
        {
            if (useStartupObservation)
            {
                _startupObservationAvailable = false;
                _startupObservedOperatingMode = null;
            }

            return blocked;
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            bool matchesStartupObservation = useStartupObservation &&
                _startupObservationAvailable &&
                _startupObservedOperatingMode == mode;
            _startupObservationAvailable = false;
            _startupObservedOperatingMode = null;
            if (matchesStartupObservation)
            {
                _wmi.ApplyWindowsPowerOverlay(mode);
                return ApplyResult.Success($"Operating mode {mode} is already active.");
            }

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
                    SetVerifiedOperatingMode(mode);
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
                    SetVerifiedOperatingMode(mode);
                    _wmi.ApplyWindowsPowerOverlay(mode);
                    return ApplyResult.Success($"Operating mode changed to {mode} through WMI.");
                }
            }

            return ApplyResult.Failure("The operating mode could not be verified.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError($"Operating mode {mode} failed", exception);
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
        if (!Enum.IsDefined(mode))
        {
            return ApplyResult.Failure("The fan mode value is invalid.");
        }

        ApplyResult? blocked = EnsureWriteAllowed(HardwareControlCapabilities.FanControl);
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
        IFanGuardWriteLease? fanGuardLease = null;
        try
        {
            if (mode is (FanMode.Max or FanMode.Custom))
            {
                fanGuardLease = _fanGuardOwnership is null
                    ? null
                    : await _fanGuardOwnership.AcquireHardwareWriteLeaseAsync(cancellationToken)
                        .ConfigureAwait(false);
                if (fanGuardLease is null)
                {
                    return ApplyResult.Unsupported("FanGuard must be active before applying this fan mode.");
                }
            }

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
                    if (fanGuardLease is not null && !fanGuardLease.IsValid)
                    {
                        return await RestoreAfterFanGuardLossAsync().ConfigureAwait(false);
                    }

                    _fanMode = mode;
                    _fanWriteOwned = mode is FanMode.Max or FanMode.Custom;
                    return ApplyResult.Success($"Fan mode changed to {mode}.");
                }
            }

            if (fanGuardLease is not null && !fanGuardLease.IsValid)
            {
                return await RestoreAfterFanGuardLossAsync().ConfigureAwait(false);
            }

            if (_capabilities.AcerWmiAvailable)
            {
                bool applied = await _wmi.SetFanModeAsync(mode, cpu, gpu, cancellationToken)
                    .ConfigureAwait(false);
                if (applied)
                {
                    if (fanGuardLease is not null && !fanGuardLease.IsValid)
                    {
                        return await RestoreAfterFanGuardLossAsync().ConfigureAwait(false);
                    }

                    _fanMode = mode;
                    _fanWriteOwned = mode is FanMode.Max or FanMode.Custom;
                    return ApplyResult.Success($"Fan mode changed to {mode} through WMI.");
                }
            }

            if (mode is FanMode.Max or FanMode.Custom)
            {
                await SetFanModeCoreWithoutValidationAsync(FanMode.Auto, CancellationToken.None)
                    .ConfigureAwait(false);
                _fanMode = FanMode.Auto;
                _fanWriteOwned = false;
            }

            return mode is FanMode.Max or FanMode.Custom
                ? ApplyResult.Failure("The fan mode could not be applied and was restored to Auto.")
                : ApplyResult.Failure("The fan mode could not be applied.");
        }
        catch (Exception exception)
        {
            if (mode is FanMode.Max or FanMode.Custom)
            {
                try
                {
                    await SetFanModeCoreWithoutValidationAsync(FanMode.Auto, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                }

                _fanMode = FanMode.Auto;
                _fanWriteOwned = false;
            }

            _logger.LogError($"Fan mode {mode} failed", exception);
            return mode is FanMode.Max or FanMode.Custom
                ? ApplyResult.Failure($"{exception.Message}; fan control was restored to Auto.")
                : ApplyResult.Failure(exception.Message);
        }
        finally
        {
            if (fanGuardLease is not null)
            {
                await fanGuardLease.DisposeAsync().ConfigureAwait(false);
            }

            _operationGate.Release();
        }
    }

    public async Task<ApplyResult> SetGpuMuxModeAsync(
        GpuMuxMode mode,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            return ApplyResult.Failure("The GPU routing mode value is invalid.");
        }

        ApplyResult? blocked = EnsureWriteAllowed(HardwareControlCapabilities.GpuMux);
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
            _logger.LogError($"GPU MUX mode {mode} failed", exception);
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
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ApplyResult? blocked = EnsureWriteAllowed(HardwareControlCapabilities.BatteryHealth);
            if (blocked is not null)
            {
                return blocked;
            }

            if (_capabilities?.BatteryControlAvailable != true)
            {
                return ApplyResult.Unsupported("Battery charge limiting is not available.");
            }

            bool applied = await _wmi.SetChargeLimitAsync(limitTo80Percent, cancellationToken)
                .ConfigureAwait(false);
            bool? readBack = applied
                ? await _wmi.ReadChargeLimitAsync(cancellationToken).ConfigureAwait(false)
                : null;
            return readBack == limitTo80Percent
                ? ApplyResult.Success(limitTo80Percent
                    ? "Battery charge limit set to 80%."
                    : "Battery charge limit set to 100%.")
                : ApplyResult.Failure("The battery charge limit could not be verified.");
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ApplyResult> SetLightingAsync(
        LightingProfile profile,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ApplyResult? blocked = EnsureWriteAllowed(HardwareControlCapabilities.Lighting);
            if (blocked is not null)
            {
                return blocked;
            }

            if (_capabilities?.LightingAvailable != true)
            {
                return ApplyResult.Unsupported("Keyboard lighting is not available.");
            }

            if (!Enum.IsDefined(profile.Effect))
            {
                return ApplyResult.Failure("The lighting effect value is invalid.");
            }

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
            if (!logo.IsSuccess)
            {
                return ApplyResult.Failure(
                    "Keyboard lighting applied, but logo lighting could not be verified.");
            }

            return ApplyResult.Success("Keyboard and logo lighting applied.");
        }
        catch (Exception exception)
        {
            _logger.LogError("Lighting update failed", exception);
            return ApplyResult.Failure(exception.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<ApplyResult> SetDeviceSettingAsync(
        DeviceSettingId setting,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SetDeviceSettingCoreAsync(setting, enabled, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<ApplyResult> SetDeviceSettingCoreAsync(
        DeviceSettingId setting,
        bool enabled,
        CancellationToken cancellationToken)
    {
        ApplyResult? blocked = EnsureWriteAllowed(HardwareControlCapabilities.DeviceSettings);
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
            _logger.LogError($"Device setting {setting} failed", exception);
            return ApplyResult.Failure(exception.Message);
        }
    }

    public async Task<ApplyResult> SetRefreshRateAsync(
        int refreshRate,
        bool enableOverdrive,
        CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ApplyResult? blocked = EnsureWriteAllowed(HardwareControlCapabilities.Display);
            if (blocked is not null)
            {
                return blocked;
            }

            int? previousRefreshRate = DisplayController.GetCurrentRefreshRate();
            bool changed = await Task.Run(
                () => DisplayController.SetRefreshRate(refreshRate),
                cancellationToken).ConfigureAwait(false);
            if (!changed || DisplayController.GetCurrentRefreshRate() != refreshRate)
            {
                return ApplyResult.Failure($"Refresh rate {refreshRate} Hz could not be applied.");
            }

            if (_capabilities?.DeviceSettings.TryGetValue(
                    DeviceSettingId.LcdOverdrive,
                    out DeviceSettingState? overdrive) == true &&
                overdrive.IsSupported)
            {
                ApplyResult overdriveResult = await SetDeviceSettingCoreAsync(
                    DeviceSettingId.LcdOverdrive,
                    enableOverdrive,
                    cancellationToken).ConfigureAwait(false);
                if (!overdriveResult.IsSuccess)
                {
                    bool rolledBack = previousRefreshRate is int previous &&
                        await Task.Run(
                            () => DisplayController.SetRefreshRate(previous) &&
                                DisplayController.GetCurrentRefreshRate() == previous,
                            cancellationToken).ConfigureAwait(false);
                    return ApplyResult.Failure(rolledBack
                        ? $"Overdrive failed; refresh rate was restored: {overdriveResult.Message}"
                        : $"Refresh rate changed, but overdrive failed and could not be restored: {overdriveResult.Message}");
                }
            }

            return ApplyResult.Success($"Display refresh rate changed to {refreshRate} Hz.");
        }
        finally
        {
            _operationGate.Release();
        }
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
        await _systemMonitor.DisposeAsync().ConfigureAwait(false);
        await _service.DisposeAsync().ConfigureAwait(false);
        _probeGate.Dispose();
        _operationGate.Dispose();
    }

    private async Task<AcerResponse?> RetryStartupServiceModeAsync(
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        TimeSpan remaining = StartupBackendRetryTimeout - stopwatch.Elapsed;
        if (remaining <= TimeSpan.Zero)
        {
            return null;
        }

        using CancellationTokenSource deadline =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(remaining);
        TimeSpan delay = StartupBackendInitialDelay;
        try
        {
            while (!deadline.IsCancellationRequested)
            {
                await Task.Delay(delay, deadline.Token).ConfigureAwait(false);
                AcerResponse? response = await TryQueryAsync(
                    AcerProtocol.OperatingMode,
                    deadline.Token).ConfigureAwait(false);
                if (response?.IsSuccess == true)
                {
                    return response;
                }

                delay = TimeSpan.FromTicks(Math.Min(
                    delay.Ticks * 2,
                    StartupBackendMaximumDelay.Ticks));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Info("Startup control backend probe reached its 10 second deadline.");
        }

        return null;
    }

    private async Task<(bool Available, bool? Enabled)> ProbeBatteryAsync(
        CancellationToken cancellationToken)
    {
        bool available = await AcerWmiClient.IsBatteryInterfaceAvailableAsync(cancellationToken)
            .ConfigureAwait(false);
        bool? enabled = available
            ? await _wmi.ReadChargeLimitAsync(cancellationToken).ConfigureAwait(false)
            : null;
        return (available, enabled);
    }

    private async Task<ServiceProbeResult> ProbeServiceCapabilitiesAsync(
        bool serviceAvailable,
        HardwareTargetProfile? profile,
        CancellationToken cancellationToken)
    {
        bool backendDiscovered = !serviceAvailable;
        if (!serviceAvailable)
        {
            AcerResponse? operating = await TryQueryAsync(
                AcerProtocol.OperatingMode,
                cancellationToken).ConfigureAwait(false);
            serviceAvailable = operating?.IsSuccess == true;
            if (AcerControlStateParser.TryReadOperatingMode(operating, out OperatingMode mode))
            {
                SetVerifiedOperatingMode(mode);
            }
        }

        AcerResponse? fan = serviceAvailable
            ? await TryQueryAsync(AcerProtocol.FanControl, cancellationToken).ConfigureAwait(false)
            : null;
        AcerResponse? lighting = serviceAvailable
            ? await TryQueryAsync(AcerProtocol.Lighting, cancellationToken).ConfigureAwait(false)
            : null;
        AcerResponse? gpu = serviceAvailable
            ? await TryQueryAsync(AcerProtocol.GpuMode, cancellationToken).ConfigureAwait(false)
            : null;

        if (AcerControlStateParser.TryReadFanMode(fan, out FanMode fanMode))
        {
            _fanMode = fanMode;
        }

        if (AcerControlStateParser.TryReadGpuMuxMode(gpu, out GpuMuxMode gpuMode))
        {
            _gpuMuxMode = gpuMode;
        }

        Dictionary<DeviceSettingId, DeviceSettingState> settings =
            await QueryDeviceSettingsCoreAsync(
                serviceAvailable,
                profile,
                cancellationToken).ConfigureAwait(false);
        if (backendDiscovered && serviceAvailable)
        {
            AcerResponse? latestOperating = await TryQueryAsync(
                AcerProtocol.OperatingMode,
                cancellationToken).ConfigureAwait(false);
            if (AcerControlStateParser.TryReadOperatingMode(latestOperating, out OperatingMode mode))
            {
                SetVerifiedOperatingMode(mode, allowStartupIdempotence: true);
            }
        }

        _lastServiceStateRead = DateTimeOffset.UtcNow;
        return new ServiceProbeResult(serviceAvailable, fan, lighting, gpu, settings);
    }

    private static DeviceCapabilities CreateCapabilities(
        DeviceIdentity identity,
        bool serviceAvailable,
        bool wmiAvailable)
    {
        HardwareTargetProfile? profile = ResolveTargetProfile(identity);
        HardwareWriteBlockReason writeBlockReason = GetWriteBlockReason(
            identity,
            serviceAvailable,
            wmiAvailable);
        return new DeviceCapabilities
        {
            Device = identity,
            TargetProfileId = profile?.Id,
            IsValidatedTarget = profile is not null,
            AuthorizedControls = profile?.AuthorizedControls ?? HardwareControlCapabilities.None,
            WriteBlockReason = writeBlockReason,
            CompatibilityMessage = GetCompatibilityMessage(identity, writeBlockReason),
            AcerServiceAvailable = serviceAvailable,
            AcerWmiAvailable = wmiAvailable,
            FanControlAvailable = HasControl(profile, HardwareControlCapabilities.FanControl) &&
                wmiAvailable
        };
    }

    internal static HardwareWriteBlockReason GetWriteBlockReason(
        DeviceIdentity identity,
        bool serviceAvailable,
        bool wmiAvailable)
    {
        if (!HardwareTargetProfileCatalog.TryResolve(identity, out _))
        {
            return HardwareWriteBlockReason.UnsupportedTargetProfile;
        }

        return serviceAvailable || wmiAvailable
            ? HardwareWriteBlockReason.None
            : HardwareWriteBlockReason.ControlBackendUnavailable;
    }

    private static HardwareTargetProfile? ResolveTargetProfile(DeviceIdentity identity) =>
        HardwareTargetProfileCatalog.TryResolve(identity, out HardwareTargetProfile? profile)
            ? profile
            : null;

    private static bool HasControl(
        HardwareTargetProfile? profile,
        HardwareControlCapabilities control) =>
        profile?.ControlProfiles.Any(profileControl => profileControl.Control == control) == true;

    private static bool HasTargetProfile(DeviceIdentity identity) =>
        HardwareTargetProfileCatalog.TryResolve(identity, out _);

    private static string GetCompatibilityMessage(
        DeviceIdentity identity,
        HardwareWriteBlockReason writeBlockReason) => writeBlockReason switch
        {
            HardwareWriteBlockReason.None =>
                $"{identity.Model} BIOS {identity.BiosVersion} has a validated hardware profile.",
            HardwareWriteBlockReason.UnsupportedTargetProfile =>
                $"No validated hardware profile exists for {identity.Model} BIOS {identity.BiosVersion}; diagnostics remain available.",
            HardwareWriteBlockReason.ControlBackendUnavailable =>
                "No supported Acer control backend is available; hardware writes are disabled.",
            HardwareWriteBlockReason.ControlNotAuthorized =>
                "The current hardware profile does not authorize this control.",
            _ => "Hardware writes are disabled."
        };

    private void SetVerifiedOperatingMode(OperatingMode mode, bool allowStartupIdempotence = false)
    {
        _operatingMode = mode;
        if (allowStartupIdempotence)
        {
            _startupObservedOperatingMode = mode;
            _startupObservationAvailable = true;
        }
    }

    private async Task RefreshServiceStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            AcerResponse operating = await _service.QueryAsync(AcerProtocol.OperatingMode, cancellationToken)
                .ConfigureAwait(false);
            if (AcerControlStateParser.TryReadOperatingMode(operating, out OperatingMode operatingMode))
            {
                SetVerifiedOperatingMode(operatingMode);
            }

            AcerResponse fan = await _service.QueryAsync(AcerProtocol.FanControl, cancellationToken).ConfigureAwait(false);
            if (AcerControlStateParser.TryReadFanMode(fan, out FanMode fanMode))
            {
                _fanMode = fanMode;
            }

            AcerResponse gpu = await _service.QueryAsync(AcerProtocol.GpuMode, cancellationToken).ConfigureAwait(false);
            if (AcerControlStateParser.TryReadGpuMuxMode(gpu, out GpuMuxMode gpuMode))
            {
                _gpuMuxMode = gpuMode;
            }

            _lastServiceStateRead = DateTimeOffset.UtcNow;
        }
        catch (Exception exception)
        {
            _logger.LogError("AcerService state refresh failed", exception);
        }
    }

    private async Task<Dictionary<DeviceSettingId, DeviceSettingState>> QueryDeviceSettingsCoreAsync(
        bool serviceAvailable,
        HardwareTargetProfile? profile,
        CancellationToken cancellationToken)
    {
        Dictionary<DeviceSettingId, DeviceSettingState> states = [];
        bool writesAuthorized = HasControl(profile, HardwareControlCapabilities.DeviceSettings);
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
            if (profile?.UnsupportedDeviceSettings.Contains(id) == true)
            {
                states[id] = new DeviceSettingState(
                    id,
                    false,
                    false,
                    null,
                    "Not supported by this hardware profile");
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
                supported && writable && writesAuthorized,
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
            ledTimeout.HasValue && writesAuthorized,
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"AcerService capability query {function} timed out.");
            return null;
        }
        catch (Exception exception)
        {
            _logger.LogError($"AcerService capability query {function} failed", exception);
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

    private ApplyResult? EnsureWriteAllowed(HardwareControlCapabilities control)
    {
        if (_capabilities is null)
        {
            return ApplyResult.Failure("Hardware capabilities have not been probed.");
        }

        HardwareTargetProfile? profile = ResolveTargetProfile(_capabilities.Device);
        HardwareControlProfile? policy = profile?.ControlProfiles.FirstOrDefault(
            profileControl => profileControl.Control == control);
        if (policy is null)
        {
            return ApplyResult.Unsupported(
                "The current hardware profile does not authorize this control.");
        }

        if (control != HardwareControlCapabilities.Display && !_capabilities.CanWriteHardware)
        {
            return ApplyResult.Unsupported(_capabilities.CompatibilityMessage);
        }

        return IsTransportAvailable(_capabilities, policy)
            ? null
            : ApplyResult.Unsupported("The profile-authorized control backend is unavailable.");
    }

    private static bool IsTransportAvailable(
        DeviceCapabilities capabilities,
        HardwareControlProfile policy) =>
        IsTransportAvailable(capabilities, policy.PrimaryTransport) ||
        (policy.FallbackTransport is HardwareTransportKind fallback &&
            IsTransportAvailable(capabilities, fallback));

    private static bool IsTransportAvailable(
        DeviceCapabilities capabilities,
        HardwareTransportKind transport) => transport switch
        {
            HardwareTransportKind.AcerService => capabilities.AcerServiceAvailable,
            HardwareTransportKind.AcerWmi => capabilities.AcerWmiAvailable,
            HardwareTransportKind.WindowsDisplay => capabilities.RefreshRates.Count > 0,
            _ => false
        };

    private async Task<ApplyResult> RestoreAfterFanGuardLossAsync()
    {
        await SetFanModeCoreWithoutValidationAsync(FanMode.Auto, CancellationToken.None)
            .ConfigureAwait(false);
        _fanMode = FanMode.Auto;
        _fanWriteOwned = false;
        return ApplyResult.Failure(
            "FanGuard became inactive during the fan write; fan control was restored to Auto.");
    }

    private async Task SetFanModeCoreWithoutValidationAsync(FanMode mode, CancellationToken cancellationToken)
    {
        try
        {
            AcerResponse response = await _service.SetAsync(
                AcerProtocol.FanControl,
                CreateFanParameters(mode, 50, 50),
                cancellationToken).ConfigureAwait(false);
            if (response.IsSuccess &&
                await VerifyIntAsync(
                    AcerProtocol.FanControl,
                    "mode",
                    (int)mode,
                    cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            await _wmi.SetFanModeAsync(mode, 50, 50, cancellationToken).ConfigureAwait(false);
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
                (JsonNode)new JsonObject
                {
                    ["fan_custom_auto"] = automatic ? 1 : 0,
                    ["fan_custom_speed"] = cpu,
                    ["fan_name"] = "CPU"
                },
                (JsonNode)new JsonObject
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

    private sealed record ServiceProbeResult(
        bool ServiceAvailable,
        AcerResponse? Fan,
        AcerResponse? Lighting,
        AcerResponse? Gpu,
        IReadOnlyDictionary<DeviceSettingId, DeviceSettingState> DeviceSettings);

    private static int? NormalizeTemperature(int? value) => value is > 0 and < 130 ? value : null;

    private static int? NormalizeRpm(int? value) => value is > 0 and < 20000
        ? (int)(Math.Round(value.Value / 100d) * 100)
        : null;
}
