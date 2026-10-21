# Windows startup troubleshooting

The installed manager exposes the same bounded test interface used during development. A successful process launch alone does not prove that POL rendered a login screen or that a character entered the world.

## Windows access from WSL

On the original development machine, PowerShell is available at `/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe`. Invoke repository scripts through their Windows UNC paths, for example `\\wsl.localhost\FedoraLinux-42\home\mzipkin\git\FFXI-Launcher\tools\windows-crash-report.ps1`. Restricted shells may require approved execution outside their sandbox for WSL interoperability. Windows administrator consent remains separate and is required for protected installation changes.

## Reproduce one account

1. Inspect running POL processes, the installed manager and its unattended scheduled task before starting anything. Preserve unrelated sessions.
2. Run `tools/windows-crash-report.ps1` during one launch. Its JSON includes Windows Application Error, Error Reporting and hang events, process/window samples, executable versions and local graphics-wrapper hashes. No matching event does not establish that the process is healthy.
3. The development script `artifacts/windows-login-control.ps1 -Operation launch -Character <configured-character>` requests a launch through the running elevated manager's authenticated `login-control` channel. This script is an ignored local artifact, not a shipped repository tool. It requires exactly one matching configured account and records the process identity returned by the manager.
4. `-Operation inspect` uses that exact tracked process and the existing window/readiness guards. `-Operation login` follows the saved login path and credential safeguards. Do not repeatedly submit credentials after an error. Successful full login requires the configured character/world and an advancing addon heartbeat.
5. If the disposable test process hangs, the same control's `-Operation close` targets only its tracked PID and creation time. Do not terminate all POL processes or replace shared installation files while a POL process is running.

The control capability token must remain private. Never include it, account credentials, or raw request files in reports. Do not overwrite account mappings, erase login caches or reinstall the manager as a first diagnostic step.

## Launcher crash when the official notice site is unavailable

On October 21, 2026, Windows .NET Runtime event 1026 recorded four launcher 1.0.22 failures (12:42:59, 17:22:17, 17:22:33 and 17:23:20 EDT) with `InvalidDataException: The official notice list format was not recognized.` The stack runs from `MaintenanceMonitor.RefreshAsync` through `MainWindow.CheckRecovery` into the asynchronous UI timer callback.

The official notice-list URL redirected to `/error/maintenance.html`, which returned HTTP 200 with Square Enix's service-unavailable placeholder. The monitor correctly rejected a response without notice links, but its catch filter covered `IOException` and omitted `InvalidDataException`, which does not inherit from `IOException`. The resulting unhandled exception closed the manager.

Version 1.0.23 handles unavailable/malformed notice responses, retains the previous snapshot and check time, reports that official notices are unavailable, and preserves the five-minute retry interval. Stale data still cannot authorize automatic recovery. Regression coverage uses a synthetic HTTP-200 maintenance page, a network failure, unchanged cache/freshness checks and successful recovery on a later valid response. The diagnostic collector also includes event 1026 and preserves unnamed event-data text containing managed exception stacks.

The repaired Windows package passed its payload verification, the maintenance regression and the full Windows dashboard/credential smoke suite. Core contracts and Linux dashboard smoke checks also passed. After installation through the normal unattended setup, version 1.0.23 was responsive with no new launcher crash events in the initial observation; all three existing POL processes retained their recorded creation times and responded. The user also confirmed that the launcher worked again. This validates the observed feed failure path, rather than every possible launcher crash.

## October 21, 2026 graphics-wrapper investigation

Windows reported 14 POL Application Error events in the observed 24-hour window for local dgVoodoo 2.87.3 `DDRAW.dll`, exception `0xc0000005`, offset `0x0004ab0a`. A single manager launch reproduced an unresponsive POL window without credential input. The same time range also contained separate older LuaCore failures; they are not evidence of the same cause.

The four preserved dgVoodoo 2.87.3 DirectX DLLs matched the official 2.87.3 release byte-for-byte. This rules out changed DLL contents in those files; it does not identify the exact software interaction responsible for the faults.

Updating the matching x86 DirectX DLLs to official dgVoodoo 2.87.5 produced a responsive PlayOnline Viewer window in the comparison launch. All new DLL hashes matched the verified release archive. This supports a wrapper compatibility or version issue, rather than file corruption. The precise underlying interaction was not isolated.

The complete update included `DDraw.dll`, `D3DImm.dll`, `D3D8.dll`, `D3D9.dll`, and the package's `dgVoodooCpl.exe`. The new configuration template retained every existing setting and added the new `DirectXExt/AlternativeScaling` default. Originals and a DLL hash manifest were preserved in the POL directory under `dgVoodoo-backup-20261021-082012`.

The first comparison process later recorded a distinct Windower 4.7.9.3 `Hook.dll` fault (`0xc0000409`, offset `0x000c739a`, at 08:22:30 EDT). Follow-up dump analysis identified a shutdown failure, described below. Subsequent sessions reached the game and published fresh matching character/world heartbeats. Those replacement sessions were observed passively; do not describe this as a completed controlled full-login test of the first comparison process or as proof that all crash paths are resolved.

## Windower Hook.dll failure on closing pre-login POL

Analysis of the saved `CrashDumps/POL/pol.exe.18220.dmp` identified a failure during normal process teardown. The process had run for 105 seconds. The user recalled closing the test POL window around that time to restart automated login, consistent with the dump. This is separate from the earlier DirectDraw startup faults.

Verified dump evidence:

- The exception is `0xc0000409` with parameter `7`, decoded by the debugger as `FAST_FAIL_FATAL_APP_EXIT`. The instruction at `Hook+0xc739a` is `int 0x29` with `ECX=7`.
- The main-thread stack includes `kernel32!ExitProcess(0)`, `ntdll!RtlExitUserProcess`, `ntdll!LdrShutdownProcess`, Hook's DLL detach code, and its C-runtime exit callbacks. This is not the account manager's forced `TerminateProcess` cleanup path.
- The exit callback saved in the caller's local at `0x001afaac` points to `Hook+0x122bb0`. That callback tests the value at `Hook+0x1cddcc` and, when nonzero, branches to the termination routine at `Hook+0xb7e29`, which calls the abort routine at `Hook+0xc7371`.
- The associated two-word thread object at `Hook+0x1cddc8` still contains handle `0x320` and thread ID `0x3bd4`. The worker-loop flag at `Hook+0x1a13c4` remains `1`.
- Initialization code at `Hook+0x2ee10` constructs this thread and registers the failing exit callback. A separate cleanup path near `Hook+0x6eb2a` clears its worker flag, signals its event, and calls the join routine at `Hook+0x35b00`, which clears the thread handle/ID on success. That state had not been reached when the exit callback ran.
- The dump contains dgVoodoo 2.87.5 DirectDraw and no loaded LuaCore or FFXI main module. There is no addon execution frame on the failing stack.

The thread constructor, join implementation and destructor guard match the Microsoft C++ `std::thread` lifecycle. **The immediate cause is destruction of a still-joinable Windower worker-thread object during POL exit, invoking termination and abort.** The most likely underlying defect is a missing or incorrectly ordered worker cleanup path when POL closes before FFXI initialization completes. The worker's private source-level name and the exact reason that normal cleanup was skipped remain unverified because matching private Hook symbols/source were not available. A joinable thread object does not prove its OS thread was still executing at process teardown.

Microsoft documents that [fast-fail uses its first exception parameter to identify the reason](https://learn.microsoft.com/en-us/cpp/intrinsics/fastfail), and its [C++ thread destructor terminates if the object remains joinable](https://github.com/microsoft/STL/blob/main/stl/inc/thread). The generic Windows wording “stack buffer overrun” is not sufficient to diagnose memory corruption for this subcode.

The initial dump-only investigation did not patch Hook, remove plugins or restart live sessions. The actual correction belongs in Windower's worker lifetime/cleanup code; any proposed fix should be validated with a disposable pre-login launch/normal-close cycle and a separate full-login/shutdown cycle. Do not blindly add waits inside DLL detach, where loader-lock restrictions apply. The saved dump and bounded debugger transcripts are retained locally for a maintainer report if requested.

### Controlled reproduction with Minibubble

At the user's request, Minibubble was gracefully shut down through the manager's authenticated session control. Nanakia, Luckystrikes and Hauplibond were recorded as protected sessions by PID plus creation time. Mini was then launched through the manager without invoking login or reading credentials.

At 08:38:10 EDT on October 21, the test helper sent one normal window-close request (`Process.CloseMainWindow`, rather than forced termination) to Mini's responsive pre-login POL window, PID 4240. The process exited with `0xc0000409`. Windows saved `CrashDumps/POL/pol.exe.4240.dmp`.

The new dump reproduced `Hook+0xc739a`, subcode `7`, the `ExitProcess(0)`/DLL-detach stack, and the joinable thread object. Its handle and thread ID were `0x314` and `0x30dc`; the worker flag remained `1`. This confirms the failure can be triggered by a normal close before login. Each protected session retained its original PID/creation time and remained responsive before and after the close trial.

The installed stable Hook matches the official stable-channel binary byte-for-byte (SHA-256 `68d059e8a9e332b274464d6b744a0732bc4a09a49c67d9c07c301c06122ccaa7`). Windower's official development channel supplies a distinct 4.7.9.6 candidate (SHA-256 `c32826605356777a8fb9507c1b238b5ede4da6cb3ef6f491424891db864a8cb5`). That candidate was tested in its own local directory with copied profile settings and no addon autoload. Native Windows module enumeration verified the actual loaded Hook path/version/hash before requesting a normal close; the installed stable Hook remained unchanged.

| Trial | Process | Result |
| --- | --- | --- |
| Stable 4.7.9.3, normal close before login | 4240 | `0xc0000409`, subcode 7, `Hook+0xc739a`; same shutdown failure |
| Isolated development 4.7.9.6, normal close before login, no addons | 2528 | `0xc0000409`, subcode 7, `Hook+0xc74ca`; same shutdown failure |
| Stable 4.7.9.3, existing manager Force close before login | 12328 | Manager verified process exit; no matching Application Error event was recorded for this PID |

The development candidate therefore does **not** fix this defect. Its dump again contains the `ExitProcess(0)`/DLL detach/C-runtime exit callback sequence. No development build was installed into the normal Windower directory.

The manager's existing Force close is a tested operational workaround for a stuck or unwanted **pre-login** POL process. It bypasses Windower's failing teardown and lets Windows reclaim the process resources. This does not repair Hook's worker cleanup; do not describe a forced exit as graceful shutdown or apply it indiscriminately to online games. No binary patch was made and no abort guard was suppressed. A source-level fix or maintainer-provided fixed Hook is still required for a clean normal pre-login exit.

At 08:53 EDT, Minibubble had completed a full login through the normal stable account-manager path, with current-build full-flow validation and a fresh matching Asura heartbeat. The other three accounts still had their original process identities and fresh matching heartbeats; all four windows responded. The final Windows report contained only the two additional controlled normal-close failures, with no extra event from the Force close trial or Mini's restoration. The [reproduction report](HOOK-CLEANUP-REPRO.md), including both builds, stack offsets and the workaround, was submitted as [Windower/Issues #1116](https://github.com/Windower/Issues/issues/1116). Full memory dumps were not uploaded.

For upgrades, follow the [official usage and configuration guidance](https://www.dege.freeweb.hu/dgVoodoo2/ReadmeGeneral/) and obtain packages from the [official releases](https://github.com/dege-diosg/dgVoodoo2/releases). DirectX DLLs are installed locally and must match the application's architecture. Keep wrapper DLL versions consistent. Back up the current DLLs, control panel and configuration; verify the archive digest and installed file hashes. Update `dgVoodooCpl.exe` from the same package. Preserve custom settings when adopting a new configuration template. POL/FFXI use the x86 DirectX files; the x64/ARM and Glide payloads are not required for this installation.
