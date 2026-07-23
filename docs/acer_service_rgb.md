# AcerService RGB

PredatorLite controls the validated PHN16-71 keyboard and logo lighting through the AcerService endpoint on `127.0.0.1:46933`.

Broader platform behavior and provenance are documented in [`acer_wmi_documentation.md`](acer_wmi_documentation.md) and [`protocol-provenance.md`](protocol-provenance.md).

## Code path

```text
LightingPage / HomePage
  -> MainViewModel.ApplyLightingAsync
  -> IPredatorPlatform.SetLightingAsync
  -> LightingPayloadFactory
  -> AcerServiceClient.SetAsync
  -> AcerService packet 100, Function="LIGHTING"
```

`AcerLightingService` must remain available. If it is unavailable, PredatorLite reports lighting as unsupported and does not attempt a fallback hardware write.

## Protocol

| Item | Value |
| --- | --- |
| Host | `127.0.0.1` |
| Port | `46933` |
| Packet magic | `ACER` |
| Set packet | `100` |
| Query packet | `20` |
| Function | `LIGHTING` |
| Encryption | Optional AES-ECB using the machine-local `HKCU\Software\Acer\XSense\AESkey` value |

The registry AES value is read at runtime. It is not committed, logged, or included in diagnostic exports.

## Supported keyboard effects

| UI mode | Acer effect | `subindex."1"` | `subindex."2"` |
| --- | --- | --- | --- |
| Static | `STATIC` | `STATIC` | `STATIC` |
| Breathing | `BREATHING` | `BREATHING` | `BREATHING` |
| Neon | `NEON` | `NEON` | `NEON` |
| Wave | `WAVE` | `WAVE` | `NEON` |
| Ripple | `SHIFTING` | `SHIFTING` | `STATIC` |
| Zoom | `ZOOM` | `ZOOM` | `STATIC` |
| Snake | `METEOR` | `METEOR` | `STATIC` |
| Disco | `TWINKLING` | `TWINKLING` | `BREATHING` |

The vendor UI may use different display labels; the values above are the wire identifiers used by PredatorLite.

## Payload rules

Dynamic keyboard effects use `device: 0`, `duration: 3`, `colortype: 1`, brightness `1..5`, speed `1..5`, one normalized `#RRGGBB` primary color, and direction `1..4`.

Four-zone static lighting uses `device: 1` and an `LEDs` array containing exactly four normalized zone colors. Logo lighting uses `device: 4`; disabling the logo sends brightness `0`.

Do not add arbitrary fields or effect identifiers without a captured request/response pair, read-back behavior, failure testing, and a target-specific capability gate.

## Persistence and query behavior

Packet `20` with `{"Function":"LIGHTING"}` is used only to probe/query the service state. PredatorLite preferences are stored in `%LocalAppData%\PredatorLite\settings.json` and are not automatically replayed as hardware writes during startup.
