# Release notes

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
