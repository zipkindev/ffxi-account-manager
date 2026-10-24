# Release notes

## 1.0.32

- On two matching POL-1039 other-terminal messages, save Maintain connection Off before acknowledging OK and closing only the affected local POL process. Show the error code and reason on the account card across app restarts; only explicitly re-enabling Maintain connection clears it. Keep FFXI-3201 on its separate timed retry path.

- Detect specific disconnect dialogs when addon panels obscure the “Error code” label. Require a bounded exact numeric code and matching failure wording; reject conflicting codes and footer-only text.
- Recover FFXI-3100 lobby failures and confirmed POL-0512 terminated-session errors through the existing confirmed-dialog and character-selection route.
- Retry the explicit POL-0019 version-communication dialog in its current process after 30, 60 and 120 seconds, with matching reads and a unique Retry button. Stop on changed screens or exhaustion without force closing or resubmitting previously entered credentials.
- Resume twice-confirmed agreement/title/character menus in the same tracked process after an interrupted recovery; require verified character/world and fresh server traffic.
- If a known HaupBridge panel blocks error inspection, close only that panel using the configured Insert console key and a twice-verified empty Windower prompt. No arbitrary commands or automation-setting changes are sent.
- Treat a briefly locked rendering-heartbeat file as unavailable evidence rather than an exception. Retain compatible login proofs and the existing three-missed-report inspection threshold. Save rotating dashboard activity messages for troubleshooting, without OCR text or request payloads.

## 1.0.31

- After an unexpected process exit, wait two minutes before automatic restart to give the server time to clear the old character session. Confirmed disconnect cleanup and scheduled maintenance keep their existing restart paths.
- Recognize FFXI-3201 only with matching held-session wording. Confirm it twice, wait one minute, confirm the same dialog again, acknowledge it and retry the configured character in the same process. Limit this to five retries; never retype POL credentials or force-close this dialog.
- Preserve reviewed login compatibility while adding the bounded server-session retry. Mini-only live trials passed both an intentionally early reconnect through 3201 and automatic restart after the two-minute delay, preserving the other three sessions. This is not a claim about a fixed server timeout. The maintenance updater still awaits its separate live patch trial.

## 1.0.30

- Prepare a guarded updater branch during full POL login: recognize version-update availability, confirmation, file checks, downloads, installation and completion before returning to the normal character/world verification route.
- Allow up to two hours for an update, with five-second progress checks and a 15-minute no-progress stop. Updater controls require two matching reads, unique in-bounds controls and an exact owned process; no blind Enter or file-repair actions are used.
- Serialize updater access and wait while another game may be using the shared installation. Unknown screens, update errors and POL/Windows restart requests preserve the process for inspection. Actual updater layouts, restart handoff and end-to-end maintenance updating still require the next live maintenance trial.
- Preserve validation for the reviewed unchanged normal login route while adding the separate updater branch. No game files were patched during preparation.

## 1.0.29

- Preserve complete-login validation across app updates when the login implementation and account mapping are unchanged. Validation now uses a deterministic fingerprint of login, OCR, input, cache and profile-resolution sources instead of the assembly build ID; release notes, artwork, packaging and unrelated UI changes do not invalidate it.
- Retain existing account/profile binding and credential requirements. Changes to fingerprinted login sources require a new complete login. Only explicitly reviewed legacy build IDs can bridge old proofs into the current baseline; unknown builds and explicit incompatible fingerprints stay blocked.

## 1.0.28

- Complete in-place disconnect recovery through character confirmation and verified world entry. The recovery route no longer stops at the character-selection checkpoint used by explicit menu calibration.
- Reuse the existing account-bound confirmation cache during recovery. An incomplete recovery can no longer report that the character and world were verified; it must establish either world entry or the need for a fresh restart.
- Require matching server-traffic evidence for recovered world entry; a menu's still-advancing rendering heartbeat cannot establish success. Returning to POL during recovery requests a fresh process before calibration-only screen limits apply.
- Retain the 30-second connection checks and third-consecutive-failure OCR threshold.

- Live validation: a Mini-only FFXI-4001 trial waited through the failed-check threshold, reused cached character confirmation, and verified world entry with fresh server traffic in the same process. The three other games were preserved and temporary network-test rules were removed. All four configured accounts passed read-only disconnect-recovery readiness checks; ordinary startup/process-exit recovery retains its current-build full-login validation requirement.

## 1.0.27

- Publish and check connection reports every 30 seconds while stats remain on their 60-second cadence. Inspect the screen only on the third consecutive missed or unhealthy connection check; each healthy, advancing report resets the count. Recheck reports before taking focus, preventing a transient file-read miss from immediately triggering OCR.
- Allow quiet packet intervals up to 90 seconds within a current report. Passive counters distinguish original packets filtered by other addons from unfiltered packets; no packet contents are stored and no extra server requests are generated.
- After an inspection finds no disconnect, defer further checks for five minutes while the same condition persists. Incoming server traffic resets the delay. Activity explains the reason for an inspection and its result.
- Count original incoming server packets as connection activity even when another addon blocks their game processing. Injected packets still cannot claim server liveness; blocked packet contents are not parsed for stats or maintenance.

## 1.0.26

- Maintain connection checks running POL sessions for confirmed POL-0250 and FFXI-4001/4002 disconnect dialogs. It dismisses the first dialog and tries the existing character-selection route; a further confirmed error closes only that tracked process and queues a fresh login.
- Track real incoming server traffic separately from addon rendering heartbeats. Healthy connections avoid repeated screen checks; a stale heartbeat alone never authorizes force close. A nonresponding process requires 90 seconds without heartbeat or server activity before timeout cleanup.
- Preserve maintenance holds, cancellation, exact process identity and credential-rejection stops. A previously validated account mapping can complete fresh login validation after an app update only as part of confirmed disconnect recovery.
- Support-center-only maintenance notices no longer block game reconnection.

## 1.0.25

- Recover from brief POL focus loss during startup and screen capture, with three recovery attempts across the login. Each attempt verifies the original process and executable; cancellation stops recovery. Input actions and credential submissions are never replayed by focus recovery.

## 1.0.24

- Requested logins can proceed when the maintenance feed is unavailable or contains unreadable notices. Known maintenance windows and unattended recovery checks still apply.
- Idle cards show No process tracked; maintenance-related launch holds are explained in Activity.
- Disable graceful shutdown and force close when no running POL process is tracked, and refresh their availability as process state changes.

## 1.0.23

- Keep the launcher running when the official maintenance feed returns a maintenance placeholder or unrecognized page. Show the unavailable status and retain cached schedules and their original check time.

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
