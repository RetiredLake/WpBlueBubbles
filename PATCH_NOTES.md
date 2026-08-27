# Patch Notes

All releases are beta software. Versions before v0.2.0.0 are retained as legacy
builds and may contain bugs fixed by later releases.

## v0.2.0.0 Beta

- Hotfix: moved growing unread/message state out of `ApplicationDataContainer.Values` and into atomic file-backed storage to prevent the settings size-limit crash.
- Hotfix: pruned stale per-chat state and moved startup diagnostics out of application settings.
- Added Blue iMessage/RCS and green SMS service styling.
- Added capability-gated iMessage availability checks.
- Added typing start and inactivity stop behavior.
- Added persistent read/unread state and server-side mark-as-read support.
- Added group photos, multi-recipient creation, rename, leave, and guarded delete.
- Added a Socket.IO stop-typing workaround for BlueBubbles Server 1.9.9.
- Added the WNS notification implementation plan; notifications remain disabled.

## v0.1.9.9 Legacy Beta

- Added an expanding, wrapping message composer with improved keyboard behavior.
- Added transparent Start, splash, package, and lock-screen branding.
- Added contact photos and stronger post-send conversation navigation.
- Improved bottom anchoring while message media loads.

## v0.1.9.8 Legacy Beta

- Opened conversations at their newest message.
- Added hold-to-save for received photos and videos.

## v0.1.9.7 Legacy Beta

- Fixed recycled message visuals leaking between conversations.
- Anchored the top bar and composer around the Lumia on-screen keyboard.

## v0.1.9.6 Legacy Beta

- Removed obsolete QR setup controls and improved QR sign-in progress.
- Corrected composer colors and several cross-chat async refresh races.
- Stabilized the message input above the on-screen keyboard.

## v0.1.9.5 Legacy Beta

- Kept the conversation header visible while composing.
- Added clickable URLs and short-lived network error messages.
- Improved share-target completion and registered-number display.
- Added initial video send and receive support.

## v0.1.9.0 Legacy Beta

- Set persistent sync defaults and refined the dedicated Settings page.
- Combined sign-out and local reset with a server-data safety confirmation.
- Disabled incomplete notification and Live Tile behavior.

## v0.1.8.0 Legacy Beta

- Corrected the full-width Lumia chat list and mobile navigation.
- Added native sync progress reporting and improved composer layout.
- Improved contact integration and per-chat Start tiles.

## v0.1.7.0 Legacy Beta

- Added dedicated Contacts and Compose pages.
- Improved photo attachment validation and conversation navigation.
- Added media context actions and initial per-chat pinning.

## v0.1.6.2 Legacy Beta

- Rebuilt the ARM package cleanly after the startup regression fix.
- Refreshed signed bundle metadata and dependencies.

## v0.1.6.1 Legacy Beta

- Fixed an ARM .NET Native XAML startup `InvalidCastException`.
- Preserved the improved mobile navigation and composer behavior.

## v0.1.6.0 Legacy Beta

- Added responsive Lumia navigation, archived-chat views, and chat actions.
- Added dedicated compose and contact flows.
- Added sync controls, safer setup, and initial attachment/share integration.
