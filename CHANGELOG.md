# Release notes

## 1.0.22

- In-game maintenance warnings appear once per world/message in Server maintenance; character cards show only their applicable official schedule.
- Upcoming warnings no longer pause login before the scheduled maintenance interval. Warnings remain tracked until their matching maintenance completes.
- Scheduled maintenance performs full graceful shutdown for live sessions. Confirmed error windows without live heartbeats can receive exact-process cleanup during active maintenance; credential rejection still requires attention before automatic retry.
- Maintained accounts become eligible for clean startup at the official window end without the former two-minute buffer. Fresh POL launches overlap before the unchanged trained login route runs.

## 1.0.21

- Startup zero HP no longer implies death. Confirmed death requires dead status plus zero HP outside Mog House/Mog Garden. Incomplete vitals display Alive with HP/MP pending.
- Addon reports immediately on load/login and follows up after 15 seconds, then resumes minute reporting. Zone and life-state changes refresh immediately.
- New live sessions no longer inherit a previous session’s dead snapshot while waiting for stats. Existing offline logout history is preserved.
- Trained login input sequences and timings are unchanged.

## 1.0.20

- Restored the trained POL route from v1.0.14: shared cached Log In clicks, control-cache learning, and the original password-field click retries and timings.
- Removed the experimental Enter-opening route. Password typing and the tuned FFXI menu/character Enter replay remain unchanged from the working baseline.
- Retained cancellation, client sizing, scalable menu matching, OCR transport corrections, and credential-free action tracing.

## 1.0.19

- Connect activates its default password field with exactly one Enter. The previous field click selected it without opening the editor.
- The pre-password route now sends one Log In Enter and one editor-opening Enter, with no burst or Enter retries. Editor confirmation occurs only after password typing completes.

## 1.0.18

- Highlighted Log In buttons can use the scalable Settings/Delete/Back menu layout when OCR misses the caption. Incomplete menu reads retry within bounds rather than failing immediately.
- This fallback applies only to Login Information; password-editor opening still sends no additional input while waiting for readiness.

## 1.0.17

- Removed the speculative pre-password Enter burst and restored the previously working password-field click. Login Information uses one Enter after exact member selection, without repeating account validation.
- Log In retries use its control; after requesting the password editor, only readiness reads continue. No additional opening input is sent into the software keyboard. Credential-free action logs record the actual route.
- POL-5311 is recognized as an explicit credential rejection; no authentication retry is performed.

## 1.0.16

- The Auto login button pulses while automatic login runs and changes to Stop auto logins. Clicking it cancels all queued and active automatic logins without closing POL or game sessions.
- Cancelled accounts stay paused for connection recovery until explicitly launched again; the button returns to normal when cancellation finishes.

## 1.0.15

- After exact-ID member selection, auto login follows the known Enter → Enter path without a second account validation or Log In button lookup.
- Missed pre-password Enter transitions retry within bounds; credential submissions are not retried. Explicit password rejection shows a credential-check warning; OCR misses do not.
- POL menu controls are matched to the left menu so cropped Login Information headings cannot be mistaken for Log In buttons.
- Added a saved native POL sizing option (640 × 480), enabled by default for auto login without changing FFXI profile resolution.
- Standardized the OCR worker’s UTF-8 input transport, including first-line BOM handling.

## 1.0.14

- OCR now selects POL or FFXI regions using the owned window’s context instead of a 1,000-pixel width cutoff.
- Scaled POL member lists retain detailed identity reads at larger sizes. Regions and returned coordinates follow actual client dimensions.
- Removed the three-times glyph-scale ceiling from bounded game-menu fallback geometry; cached controls remain tied to the validated window size.

## 1.0.13

- World Map and Zilart now use the selected full artwork, centered on the female figure and chocobo rider in the main area.
- World Map, Zilart and Blue Illustration now form one continuous image across the left panel and right background, with adjoining crops and matching shading through resizing.

## 1.0.12

- Original FFXI now pairs the approved anniversary character illustration on the left with its original artwork on the right.
- The left crop focuses on the character’s face and upper body while preserving image proportions.

## 1.0.11

- Promyvion now uses the selected Amano illustration as its primary right background, retaining the original zone artwork on the left.

- Abyssea now pairs Cavernous Maw on the left with Abyssea artwork across the right.
- Removed the standalone Cavernous Maw theme; existing selections resolve to the merged Abyssea theme.

## 1.0.10

- Added FFXI · Amano · Autumn, pairing the character illustration on the left with the autumn artwork across the right.
- Artwork themes now support independent side and background images with aspect-preserving crops.

## 1.0.9

- Widened the maintenance advance-notice hour editor so its full value remains readable alongside the spinner buttons, including the maximum 168 hours.

## 1.0.8

- Added current main job, subjob, their levels and master level to character cards, formatted as ML 46 99DRK/57WAR.
- Job telemetry shares the once-per-minute reporting cycle and retains the last report offline.
- Master level is read passively from Windower’s server-stat packets; missing values are shown as unknown.
- Companion addon updated to 0.3.5; running games need an addon reload to enable job reports.


## 1.0.7

- Moved card-alert display controls into Server maintenance.
- Added one global advance-notice setting from 0 to 168 hours, saved automatically and applied to every account’s applicable notices.
- Zero hours shows active maintenance only. The main schedule and reconnect protections remain independent of card display preferences.


## 1.0.6

- Added a saved Show maintenance on cards option; the main Server maintenance section remains visible.
- Card notices show applicable maintenance within 12 hours, preferring a matching-world notice over a global notice.
- Removed empty world-status rows and hid card panels when no applicable alert is due. Scheduling and reconnect holds remain independent of display preferences.


## 1.0.5

- Made per-character maintenance panel backgrounds 50% transparent while preserving full-opacity text.


## 1.0.4

- Reduced the shading over landscape cards so the image is the primary card background.
- Added a saved artwork rotation that uses every landscape before repeating.
- Keeps visible card images distinct until the landscape pool is exhausted, and repairs duplicate saved selections.


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
