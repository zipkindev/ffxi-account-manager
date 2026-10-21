# Windower pre-login normal-close cleanup failure

Investigation, October 21, 2026. Submitted as [Windower/Issues #1116](https://github.com/Windower/Issues/issues/1116). Full dumps remain local.

Reviewed debugger transcripts, selected Windows fault records and normal-close test results are [published directly on the issue](https://github.com/Windower/Issues/issues/1116#issuecomment-6316974337). Personal filesystem paths and account details were removed. The issue body links to this evidence instead of asking maintainers to request it.

## Reproduction

1. Start configured POL through Windower and wait for the responsive PlayOnline Viewer window.
2. Remain before account login; no credential input or FFXI world entry is needed.
3. Close the owned POL main window normally. The controlled test uses `Process.CloseMainWindow()` and waits for process exit. It does not kill the process.

Actual result: Windows records a `Hook.dll` failure, `0xc0000409`, first exception parameter `7` (`FAST_FAIL_FATAL_APP_EXIT`). Expected result: normal process exit after Windower releases its workers.

## Tested builds

| Build | Source | SHA-256 | Result |
| --- | --- | --- | --- |
| Stable 4.7.9.3 | [Official stable Hook](https://files.windower.net/4/stable/Hook.dll) | `68d059e8a9e332b274464d6b744a0732bc4a09a49c67d9c07c301c06122ccaa7` | Reproduced at RVA `0xc739a` |
| Development 4.7.9.6 | [Official development Hook](https://files.windower.net/4/dev/Hook.dll) | `c32826605356777a8fb9507c1b238b5ede4da6cb3ef6f491424891db864a8cb5` | Reproduced at RVA `0xc74ca` |

The development test used a separate Windower directory and empty addon autoload. Native module enumeration confirmed that POL loaded the development DLL from that directory, with the hash above. Both tests used dgVoodoo 2.87.5 x86 DirectX DLLs and Windows build 26300. The installed Windower directory was unchanged. Three unrelated online sessions preserved their PID/creation time and remained responsive.

## Stable-build dump evidence

The main-thread stack runs from POL into `kernel32!ExitProcess(0)`, `ntdll!RtlExitUserProcess`, `ntdll!LdrShutdownProcess`, Hook DLL detach, and Hook's C-runtime exit callbacks. Relevant Hook frames, from the fault downward:

```text
Hook+0xc739a   int 0x29, ECX=7
Hook+0xcbd23   exit callback dispatch
Hook+0xcbb0a
Hook+0xcbdcf
Hook+0xc3064
Hook+0xc2faf
Hook+0xc3152
Hook+0xc32bc
Hook+0xb4164
Hook+0xb44d6
Hook+0xb457c
...
ntdll!LdrShutdownProcess
ntdll!RtlExitUserProcess
kernel32!ExitProcess
```

The captured callback is `Hook+0x122bb0`. It tests the thread ID at `Hook+0x1cddcc`, branching to the terminate routine at `Hook+0xb7e29` when nonzero. That routine calls abort at `Hook+0xc7371`, which generates the fast-fail.

In the controlled stable trial, the thread object at `Hook+0x1cddc8` retained handle `0x314` and thread ID `0x30dc`; its worker flag at `Hook+0x1a13c4` was still `1`. Initialization near `Hook+0x2ee10` constructs the worker and registers its exit callback. A separate cleanup path near `Hook+0x6eb2a` clears the flag, signals the event, and calls join at `Hook+0x35b00`, clearing the thread object after successful join.

These patterns match a C++ `std::thread` destructor terminating because it is still joinable. The immediate failure is established by the dump and disassembly. The source-level worker name and why its stop/join path is skipped require matching private symbols/source. Joining from DLL detach itself can introduce loader-lock problems; cleanup should be reviewed at the appropriate lifecycle point.

## Workaround and retained evidence

The account manager's existing exact-process Force close avoided a corresponding Windows crash event in the separate pre-login trial. This is forced termination, not a corrected graceful exit.

Saved dumps and debugger transcripts are retained locally. Full dumps contain process memory and have not been uploaded or included in this repository. The report uses relative module offsets rather than guessed private symbol names. Some debugger stack frames lack private unwind information; call sites and callback state were checked against the loaded binary's instructions.
