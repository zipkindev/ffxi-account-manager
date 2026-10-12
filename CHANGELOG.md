# Release notes

## 1.0.3

- Reworked all 12 artwork themes with aspect-preserving crops for a tall left panel and matching right-side background.
- Made character cards more translucent when Landscape cards is off, keeping controls and maintenance details readable.
- Removed Paper, Misareaux Coast, Tavnazian Safehold and Lufaise Meadows. Removed selections fall back safely to Midnight.
- Verified bundled artwork, including Abyssea, and added a full theme-preview renderer.


## 1.0.2

- Added a compressed, self-contained Windows executable with no loose DLL requirement.
- Embedded installer, OCR helper, addon and license payloads, materialized automatically when needed.
- Updated build identity for login validation so it works in both folder and single-file packages.

Packaging changes require fresh complete login validation before unattended recovery for this build.


## 1.0.1

- Added the MIT code license and separate FFXI artwork/trademark and dependency notices.
- Added a Licenses & credits view and included license files in Windows packages.
- Updated distribution and installation guidance in the README.


## 1.0.0

- Account launching: named Windower profiles, per-account Auto login behavior, overlapping POL startup, learned action paths, and recovery when early menu input is missed.
- Session management: discovery and attachment of running characters, connection monitoring, bulk shutdown, cancellation, and cleanup of disconnected processes.
- Maintenance: official global/world notices, local time-zone display, per-character schedules, and in-game emergency warnings.
- Dashboard: compact cards, drag-to-reorder with sliding previews, saved themes, original FFXI artwork, landscape cards, and clipboard commands.
- Vana’diel: shared time, weekday element icon and moon phase; per-character zone weather with single/double element icons. Stats and weather report once per minute.
- Windows setup: encrypted credentials and a protected scheduled-task installation for routine elevated launches.
- Release tracking: a clickable app version opens these bundled notes; builds share one version file and CI checks release tags against it.

Validation scope: automated checks cover core policies, UI and addon behavior. Live login reliability depends on the account/profile and desktop focus. The latest shutdown-cancellation and early-input recovery changes still need live verification. Linux/SteamDeck launch automation remains planned.

## 0.2.7

- Established the Windows account dashboard, companion addon, login automation and maintenance integration that this release extends.

## 0.2.6

- Added account configuration, encrypted credential storage, profile launching, running-session discovery, bulk controls and maintenance scheduling policies.
