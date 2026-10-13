# Dashboard and Input Mapper redesign validation

## Automated verification

- Release build of the entire solution on Windows CI: passed, zero warnings and zero errors. The Linux Windows-targeting cross-build also passed.
- Portable regression suite: 24 passing tests. It compiles the actual production layout, persistence, physical-device selection, presentation coordinator, autosave, and numeric-draft code.
- Portable coverage includes breakpoint boundaries, all default layouts, deterministic collision cascades, preserved gaps, presets, atomic persistence, independent membership, zero/one/multiple wheels, identical models, auxiliary pedals, stale preferences, 30-Hz/2-Hz/hidden pacing with a fake clock, immediate restoration, mounted views, bounded snapshots, debounce, serialized writes, revision ordering, validation/write failures, and shutdown flushing.
- Windows WPF/runtime suite: **58 passing tests**, with zero failures or skips. The Windows suite covers board drag cancellation/drop/reset/navigation, vertical mouse movement and legacy aliases, malformed shortcuts, cached raw-device reselection and control replacement, foreground guards, release on disable/disconnect/profile changes, preservation of inactive edits, rejected writes, inert preview adapters, profile cards/navigation/starter mappings, multiple linked applications, and settings migration.
- Windows rendering coverage uses both themes at narrow, default, and wide widths, including editable/empty dashboards, mapping details, module switches, and disabled pages. Set `LOGIWHEELFORGE_RENDER_OUTPUT` to save the PNGs. CI uploads the images alongside TRX and test-host diagnostics.

## Local runner limitation

The local worker is Linux ARM64. A .NET 10 SDK was installed temporarily to compile Windows targets and execute the portable tests. The full WPF/runtime test invocation aborted before executing tests because VSTest could not find an x64 .NET host. This is a concrete platform prerequisite failure, not a passing test run.

The Windows workflow uses an x64 host, a 180-second connection timeout, serialized test execution, a bounded test session, and diagnostic logging. The complete Windows run succeeded, resolving the baseline runner blocker for validation: **58 Windows tests + 24 portable tests = 82 passing tests**. See the [verified Windows run](https://github.com/T-Craft-Platforms/logi-wheel-forge/actions/runs/38325725468).

Both-theme rendering artifacts were downloaded and visually inspected at 608, 944, and 1320 DIP, including the dashboard, edit controls, empty state, module welcome panels, profile setup, and mapping details. Rendering uses a real WPF window and dispatcher, with a Canvas capture host to avoid clipping wider images to the CI monitor. Review found and corrected visibility fallbacks, editable-control resets, and unused space in the narrow mapping layout.

## Hardware acceptance still required

On a Windows PC with supported wheels:

1. Verify no/one/several wheels, including two identical models. Change selection from both Settings and the dashboard; connect/disconnect while Settings is open. Confirm the name, raw controls, LEDs, and resistance all follow the same physical instance. USB pedals must never appear in the wheel picker.
2. In a foreground linked application, exercise steering steps, pedal holds, button actions, keyboard chords, horizontal/vertical movement, and scrolling. Switch focus, disable the module, disconnect the wheel, and edit the active profile; confirm every held output releases.
3. Exercise Preview while the application's window is foreground. Confirm it never emits Windows input or physical resistance, including hold-target and detent rules.
4. Check LED output and supported spring/damper/detent/hold-target effects on each target model. Check unavailable capabilities and exclusive-access failures have useful explanations.
5. Verify visual behavior and keyboard focus at the minimum, default, and wide window sizes in both themes. Inspect CI render artifacts and the actual application, including dragging, empty boards, disabled pages, autosave field errors, and mapping details.
6. Unfocus, minimize, and hide to the tray while telemetry and mapping are active. Confirm backend execution, discovery, persistence, and tray commands continue; restoring/focusing must show current data immediately.

No physical wheel, LED, or resistance check was performed on the Linux worker.
