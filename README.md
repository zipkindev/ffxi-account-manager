# FFXI Account Manager

Windows-first account management and launch automation for Windower and Final Fantasy XI, built with Avalonia and .NET 10. Shared UI and core code support a future Linux/SteamDeck adapter.

## App walkthrough

These screenshots show the actual application UI with example accounts and no saved passwords. Captured in the cross-platform renderer; Windows-only launch controls are disabled in these examples.

POL auto login defaults to a native 640 × 480 client window. Toggle this under Launcher paths to retain another POL size; FFXI still uses the selected Windower profile resolution. After exact member selection, the app follows the known Enter path and only types credentials once the password editor is ready.

### Account dashboard and themes

![Account dashboard with an FFXI artwork theme](docs/images/dashboard.png)

Choose one of four dark UI themes or 12 artwork themes, including Abyssea. Expanded windows show a tall cropped art panel with a background behind the accounts; Amano · Autumn, Abyssea, Promyvion, and Original FFXI pair two different images. Disable Landscape cards for more translucent account cards; enable it for prominent landscape images with minimal shading. Shuffle uses every image before repeating and avoids duplicate visible cards while enough images are available. Images keep their aspect ratio. Drag the handle on each card to reorder accounts. **Auto login** controls whether a character's Launch profile button completes login. **Maintain connection** enables recovery; **Schedule server maintenance** plans downtime around applicable notices.

World Map, Zilart and Blue Illustration use a continuous panoramic background: the left panel reveals the adjoining artwork rather than repeating it.

[See every theme at full size](docs/themes/README.md), including the narrow-window Abyssea view.

### Configure an account

![Account editor with example SE ID and Windower profile](docs/images/account-setup.png)

Provide the SE ID and password, choose a Windower profile, and confirm character/world details. Already-saved POL credentials are reused; the app does not create a Square Enix account or POL member.

### Bulk controls

![Expanded bulk account controls](docs/images/bulk-actions.png)

Select cards, then expand **Bulk actions** to apply settings or request graceful shutdown. Selection is independent of Auto login. Load the companion addon in running characters before using IPC shutdown controls.

## Features

- Configure accounts with encrypted credentials, named Windower profiles, and separate Auto login, Maintain connection and maintenance scheduling switches.
- Launch profiles individually or in batches. Per-account **Launch profile** follows the Auto login switch; bulk **Open profiles** controls leave login to you.
- Discover and attach existing character sessions using process ownership and companion-addon character/world reports.
- Complete POL and FFXI login with learned action paths, exact-character verification and guarded input. Keep keyboard and mouse idle during foreground login automation.
- Gracefully shut down individual or grouped sessions, cancel pending shutdowns, and clean up disconnected POL processes. Explicit Force close is available for attached hung sessions.
- Track official global/world maintenance notices in your local time zone, schedule downtime and pause reconnection attempts during maintenance. The companion also observes in-game emergency warnings. In **Server maintenance**, choose whether to show card alerts and set one global 0–168-hour advance-notice threshold (default 12; 0 shows active maintenance only). Every account uses that threshold for its applicable notices. Matching-world notices take precedence over global notices; the main section retains the full schedule and in-game alerts. These display preferences do not change scheduling or reconnect protection.
- Choose saved UI/artwork themes, landscape account cards, and drag-to-reorder account order.
- Show current jobs and levels in the form **ML 46 99DRK/57WAR**, with last-observed jobs retained offline.
- Show shared Vana’diel time, weekday element and moon phase; display per-character zone weather with single/double element icons.

Character stats, jobs and weather report once per minute. Master level comes from cached/passively observed server-stat packets; an unavailable master level is displayed as unknown. Session health heartbeats and IPC command checks remain separate, and logout is reported immediately. Offline cards retain their last observation rather than claiming current weather.

## Downloads and requirements

Download the Windows x64 ZIP from [GitHub Releases](https://github.com/zipkindev/ffxi-account-manager/releases/latest). Build artifacts are also available from GitHub Actions (sign-in may be required). The standalone `.exe` needs no ZIP extraction or separate .NET installation. A folder ZIP is also available; extract its entire contents before launching it. The self-contained build does not require a separate .NET installation.

You need Windows x64, an existing working Windower/PlayOnline/FFXI installation, and configured POL members. This app does not include the game, Windower or a subscription.

## Windows setup

Run the standalone executable directly, or keep all files together if using the folder ZIP. The standalone app automatically materializes its OCR, installer, addon and license support files under `%LOCALAPPDATA%\FFXILauncher\app-payload`, while .NET extracts bundled native libraries into its Windows cache. The protected Windows installation creates a scheduled-task launch shortcut; setup requires administrator consent, while routine launches use the installed task.

Windower and POL paths are detected automatically, with manual overrides in **Launcher paths**. Configure the SE ID and password; character/world and member mapping must be resolved before complete automated login. POL/local member passwords are conditional on your existing POL configuration. Unattended OTP login is not supported.

Select **Deploy addon + enable auto-load** to install FFXIManager and add its startup entry to Windower's `scripts/init.txt`. Existing script commands and addon data are preserved. For already-running games, enter:

```text
//lua load FFXIManager
```

After an addon update, use `//lua reload FFXIManager` in each running game. Future sessions load it automatically. Closing the manager leaves games running.

Account metadata is saved under `%LOCALAPPDATA%\FFXILauncher\accounts.json`, and passwords under `vault/`, encrypted with Windows DPAPI for the current Windows user. Do not commit local account data, vaults or addon control files. These paths and local build/agent artifacts are excluded by `.gitignore`.

## Versioning and releases

`Version.props` is the single application version source. Click the version beneath the app title for bundled [release notes](CHANGELOG.md), including major updates and validation scope.

For the next release:

1. Update the major.minor.patch version in `Version.props`.
2. Add the matching section to `CHANGELOG.md`.
3. Run `python tools/check-release.py` and the checks below.
4. Use the matching Git tag `vX.Y.Z`. GitHub CI rejects a tag that differs from the app version or missing release notes.

CI builds and validates the Windows package and runs isolated Lua addon checks. It uploads build artifacts; it does not automatically publish a GitHub Release.

## Build and validation

Install .NET 10 SDK, then run:

```sh
dotnet build FFXILauncher.sln
dotnet run --project tests/FFXILauncher.Core.Tests
dotnet run --project tests/FFXILauncher.Desktop.Smoke
python tools/check-release.py
```

Publish a self-contained Windows build with PowerShell:

```powershell
./tools/publish-windows.ps1
# Compressed standalone executable:
./tools/publish-standalone.ps1
```

Test the companion with Lua and an isolated temporary directory:

```sh
mkdir -p /tmp/ffxi-manager-addon-test/data
lua tests/test_manager_addon.lua /tmp/ffxi-manager-addon-test/
```

Automated checks cover configuration, identity, maintenance policies, IPC authentication/session binding, UI behavior and credential handling. Complete successful Windows login records account/profile/build validation; unattended recovery and scheduled downtime depend on that proof. Recognized login errors stop automation. The latest shutdown cancellation and early-input recovery changes still need live verification. Linux/SteamDeck launch automation and reliable extraction of saved POL account identities remain planned.

Artwork and icon provenance is listed in `src/FFXILauncher.Desktop/Assets/Themes/SOURCES.md` and `Assets/Weather/SOURCES.md`. See [implementation details](docs/IMPLEMENTATION.md) for architecture and remaining work.

## License and distribution

Original code and documentation are licensed under [MIT](LICENSE), copyright 2026 zipkindev and contributors. Keep the license and copyright notice with redistributed copies.

FFXI artwork, game icons and trademarks are excluded from MIT and remain subject to their owners’ terms. Read [the artwork notice](ARTWORK-NOTICE.md) before distributing artwork-inclusive builds: the linked Square Enix policy does not explicitly address desktop bundling. MIT does not grant commercial rights to those assets. This project is independent and is not endorsed by Square Enix.

Dependencies retain their own licenses. [Third-party notices](THIRD-PARTY-NOTICES.md) and full texts in [licenses/](licenses/) are included in Windows packages. Open **Licenses & credits** beneath the app title to read these notices offline.

FINAL FANTASY XI © 2002 - 2020 SQUARE ENIX CO., LTD. All Rights Reserved.
FINAL FANTASY is a registered trademark of Square Enix Holdings Co., Ltd.
