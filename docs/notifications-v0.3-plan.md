# Notification Goal Plan

## Decision

Use BlueBubbles webhooks through a small authenticated relay that delivers WNS
toast notifications. Keep local periodic polling as an optional delayed fallback.
Do not make Firebase the Windows client transport: Windows 10 Mobile has no
appropriate first-party FCM client path, while WNS is native to UWP and the
Microsoft Store app identity.

## Phase 0: Identity And Safety

1. Associate the UWP package with its Microsoft Store identity and record its
   Package SID, PFN, WNS client ID, and secret outside source control.
2. Define a signed device-registration request. Store only the WNS channel URI,
   an opaque device ID, expiration, and last-seen time in the relay.
3. Add replay protection and a shared webhook secret. Never put the BlueBubbles
   server password into WNS payloads or relay logs.

## Phase 1: Foreground Realtime

1. Promote the minimal Socket.IO transport used by typing-stop into one managed
   foreground connection.
2. Subscribe to `new-message`, `updated-message`, `chat-read-status-changed`, and
   `typing-indicator` events.
3. Reconnect with bounded exponential backoff and refresh only the affected chat.
4. Keep the existing 15-second REST poll as a temporary recovery check, then
   lengthen or disable it after Socket.IO proves stable on Lumia hardware.

## Phase 2: WNS Push Relay

1. Create a small HTTPS relay with endpoints for device registration,
   deregistration, and BlueBubbles webhook ingestion.
2. Translate `new-message` webhooks into WNS toast XML containing only sender,
   preview, chat GUID, and message GUID. Use `chat=<escaped-guid>` activation.
3. Refresh WNS OAuth tokens server-side and remove expired channel URIs when WNS
   returns an invalid or gone response.
4. Register BlueBubbles webhook events for new messages and message updates. Do
   not expose the relay's management endpoints publicly without authentication.

## Phase 3: UWP Notification UX

1. Request a WNS channel after sign-in and upload it to the relay.
2. Route toast activation to the exact conversation using the existing chat GUID
   activation path.
3. Update unread state and the primary Live Tile from server-backed read state.
4. Add per-chat mute controls before enabling notifications by default.
5. Remove the relay registration on sign-out/reset and renew channels before the
   WNS expiration date.

## Phase 4: Local Fallback

1. Offer an opt-in `TimeTrigger` background task for delayed checks when WNS is
   unavailable. Treat the platform's scheduling interval as best-effort, not
   realtime delivery.
2. Persist the last observed message GUID per chat to avoid duplicate toasts.
3. Keep foreground Socket.IO and REST reconciliation authoritative after resume.

## Verification Gates

- No notification is emitted for a message already marked read.
- One incoming message produces at most one toast and one unread increment.
- Tapping a toast opens its exact chat on PC and Lumia.
- Sign-out removes local channel state and relay registration.
- Expired WNS channels are renewed without requiring an app reinstall.
- No server password, message body, or contact data appears in diagnostic logs.
- ARM, x86, and x64 Release builds pass after notification plumbing is enabled.
