# Patch Notes

All releases are beta software. Versions before v0.2.0.0 are retained as legacy
builds and may contain bugs fixed by later releases.

## v0.3.0.0 Beta

- Added a single dark message action menu with Delete, Forward, Copy, and Save shown only when each action applies.
- Added confirmed server-side message deletion for Private API servers; deletion does not unsend the message.
- Added forwarding for text and multiple attachments through the existing Compose recipient flow.
- Replaced the OLED toggle with System, Light, Blue, and Dark themes; System is the default and follows Windows.
- Preserved Messenger blue or the optional Windows accent for outbound messages while incoming messages remain gray.
- Added a manual GitHub update checker that downloads the latest regular-release app bundle and opens the Windows installer.
- Fixed desktop keyboard handling so Enter is always consumed as Send, including failed sends, while Shift+Enter inserts a newline.
- Added physical-keyboard focus routing so typing in a conversation enters the composer unless another text field is active.

## v0.2.2.1 Beta

- Added an optional, persisted Send read receipts toggle. It is off by default.
- Kept local unread tracking active when server read receipts are disabled.
- Fixed message and media context menus so desktop right-click no longer opens overlapping menus or crashes.
- Fixed touch-and-hold Copy and Save menus closing before an action could be selected on Windows 10 Mobile.
- Updated the credits link to the WpBlueBubbles GitHub project.

### Coming soon

- Notifications and Live Tiles.
- Read receipts and incoming typing indicators.

## v0.2.2.0 Beta

- Made share-target files durable before upload so images remain attached throughout Compose.
- Resolved selected contacts to existing conversations before enforcing the new-chat text requirement.
- Closed Compose and the Windows share sheet immediately after a successful send.
- Navigated to the destination conversation independently of the subsequent chat refresh.
- Returned to Chats with a clear error if the destination conversation could not be opened.
- Prevented refresh failures from leaving Compose or the share sheet hanging after a successful send.
- Preserved original shared filenames and removed temporary share copies after completion.

## v0.2.1.9 Beta

- Restored reliable image rendering by returning to the proven direct attachment path.
- Prevented recycled image controls and hidden video controls from invalidating successfully loaded pictures.
- Preserved on-demand video playback, which was confirmed working in the release candidate.
- Made physical-keyboard Enter send on desktop and Shift+Enter insert a newline; phone Return remains newline-only.
- Made desktop right-click consistently offer Copy for messages and Save for media.
- Added a persistent 3/5/10/15/30-second message fetch setting, defaulting to five seconds.
- Kept search, compose, and conversation actions visible together in wide desktop layouts.
- Removed focused white backgrounds and borders from both message composers.
- Corrected Windows accent mode so only outbound bubbles use the selected accent; incoming bubbles remain gray.
- Added clearer notification placeholder text and a manual Private API status refresh.

## v0.2.1.8 Beta

- Restored image rendering with lazy authenticated downloads for visible messages, without blocking chat history on media.
- Limited concurrent image downloads and reused temporary image files during the session.
- Made physical-keyboard Enter send on desktop and Shift+Enter insert a newline; phone Return remains newline-only.
- Added a persistent 3/5/10/15/30-second message fetch setting, defaulting to five seconds.
- Kept search, compose, and conversation actions visible together in wide desktop layouts.
- Removed focused white backgrounds and borders from both message composers.
- Changed the normal notification placeholder to "Notifications and Live Tiles coming soon" while preserving diagnostics copy in Developer mode.
- Added a manual Private API status refresh when the helper is unavailable or its status cannot be read.
- Fixed accent mode so outbound bubbles immediately use the actual Windows accent color; incoming bubbles remain gray.

## v0.2.1.7 Beta

- Restored the pre-v0.2.1.5 media path: chats load attachment metadata immediately and visible media streams from BlueBubbles on demand.
- Removed eager sequential media downloads and local media conversion from chat loading.
- Added bounded retries to safe server reads and chat-list queries without retrying sends, deletes, renames, or other mutations.
- Reused one server-info response during connection setup instead of issuing duplicate requests.
- Made a successful authenticated connection survive an initial chat-sync failure; normal polling retries the sync automatically.
- Kept manual Save media as an explicit on-demand download.

## v0.2.1.6 Beta

- Anchored the newest message above the Lumia keyboard after its final layout pass.
- Avoided the Lumia close/reopen keyboard flicker after sending; PC still refocuses for continued typing.
- Made OLED black the default for new and reset installations.
- Applied compact header sizing to Settings, Contacts, and New message pages.
- Added raw exception diagnostics only when Developer mode is enabled.
- Removed the redundant public video-decoding error while retaining the unavailable label.
- Downloaded pictures and videos into a local media cache before rendering.
- Restored gray incoming bubbles; sent bubbles remain Messenger blue or Windows accent.
- Corrected the phone QR preview rotation in the opposite direction.
- Expanded sign-out/reset to remove local files, temporary media, caches, unread state, contacts, and credentials.

## v0.2.1.5 Beta

- Added a GroupMe-inspired compact phone layout with an optional Larger UI mode.
- Kept the Lumia keyboard focused while a message is sending and disabled input without removing focus.
- Made physical-keyboard Enter send on PC and Shift+Enter add a line; mobile Return always adds a line.
- Added clearer offline, unreachable-server, missing-conversation, send, rename, and delete errors.
- Added multiple attachment selection and multi-file share-target sending.
- Cached downloaded videos locally before playback and added an explicit unsupported-video error.
- Made unread state phone-local and treats the initial post-setup chat list as read.
- Sorted Chats newest-first and made search appear only when requested from the header.
- Added a secondary Windows accent shade for sent messages while retaining Messenger blue otherwise.
- Hardened legacy settings migration so malformed values cannot repeatedly crash startup.
- Corrected the phone QR camera preview rotation.

## v0.2.1.0 Beta

- Reorganized connected Settings into Sync, Theme, Server details, Reset, and Credits sections.
- Added persistent OLED-black and Windows accent-color themes with immediate updates.
- Unified incoming and outgoing bubbles under the selected Messenger blue or accent color.
- Added sanitized server details, Private API state, and a reserved Developer mode toggle.
- Added contact-resolved sender labels to incoming group-chat messages.
- Added a full-screen picture viewer that closes by tap or system Back.
- Added cached contact and group photos to unique per-conversation Start tiles, with safe logo fallback.
- Removed all visible composer service-availability status text while retaining internal routing checks.

## v0.2.0.1 Beta

- Matched the official BlueBubbles full-sync behavior by hiding chats whose latest message falls outside the selected timeframe.
- Made the server's chat-level `hasUnreadMessage` value authoritative so read state follows the Mac.
- Added paginated chat loading beyond the previous 1,000-chat ceiling.
- Prevented stale overlapping chat refreshes from replacing a newer timeframe result.

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
