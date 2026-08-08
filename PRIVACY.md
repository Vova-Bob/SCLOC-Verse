# Privacy Policy for SCLOC-Verse

**Effective date:** July 3, 2026

**Contact:** Ukrainian Space Fleet Discord community — https://discord.gg/VdQBscHRCB  
Project author: VALDEUS (Vova-Bob)

## 1. Overview

SCLOC-Verse respects your privacy. This policy explains what data the application collects, why it is needed, and how it is protected.

## 2. Data we collect

### 2.1. Account data (SCLOC Account)

To create a SCLOC Account, the application uses Discord OAuth with the minimum required scope: `identify`.
The following data is collected:

- `discord_user_id` — your unique Discord identifier; the basis of the SCLOC Account.
- `username` / `global_name` — displayed in the account button, account dialog, and tooltip.
- `avatar_url` — used as a visual identifier for your SCLOC Account.

### 2.2. Installation metadata

To improve stability and compatibility, the application sends the following technical information:

- `install_id` — a locally generated GUID.
- `machine_id` — the local computer name (`Environment.MachineName`).
- `platform` — the operating system (Windows).
- `os_version` — the OS version string.
- `app_version` — the SCLOC-Verse build version.
- Session timestamps (`session_id`, `started_at`, `ended_at`) in UTC.

### 2.3. Data we do not collect

SCLOC-Verse **does not collect or store**:

- Your email address (see Section 3).
- Passwords or payment information.
- Physical addresses.
- Biometric data.
- Your Discord guild list.
- Behavioral telemetry or in-app activity history.

## 3. Note on email

SCLOC-Verse requests only the `identify` scope from Discord. During the OAuth exchange, Supabase GoTrue may include an `email` scope for technical reasons related to its built-in Discord provider. The option `EXTERNAL_DISCORD_EMAIL_OPTIONAL` is enabled, so authentication succeeds even if no email is present.

**SCLOC-Verse does not store, display, or use your email address for any feature.**

## 4. How we use your data

- Account data is used for sign-in, profile display, and binding the installation to your account.
- Technical metadata is used for diagnostics, version analytics, compatibility analysis, and session history (DAU/WAU/MAU).

## 5. Where data is stored

| Data | Location | Notes |
|---|---|---|
| Account data | Supabase `auth.users` | Managed by Supabase GoTrue |
| Installation metadata | Supabase `app_installations` | SCLOC-Verse project table |
| Session history | Supabase `sessions` | SCLOC-Verse project table |
| Local tokens | `%LocalAppData%\SCLOCVerse\.auth` | Encrypted with Windows DPAPI |
| `install_id` | `%LocalAppData%\SCLOCVerse\install-id` + HKCU registry | Local device identifier |

All data in Supabase is protected by Row Level Security (RLS) policies.

## 6. Sharing with third parties

SCLOC-Verse does not sell or share personal data with third parties. We only use infrastructure services:

- **Discord** — for OAuth authentication.
- **Supabase** — for storing account, installation, and session data.

## 7. Security

- Local tokens are encrypted using Windows DPAPI.
- Database access is restricted by Supabase RLS policies.
- The application does not use DLL injection, does not interfere with the game process, and does not interact with anti-cheat systems.

## 8. Your rights

You have the right to:

- Request information about your data.
- Correct inaccurate data (your name/avatar is managed in Discord settings).
- Delete your SCLOC Account and all associated data.
- Revoke SCLOC-Verse access to Discord OAuth through your Discord settings.

To delete your account, please contact the project authors via the Discord community.

## 9. Changes to this policy

If this policy changes, an updated version will be published on this page. Significant changes to data collection will be communicated to users separately.

## 10. Open source

SCLOC-Verse source code is available on GitHub:
https://github.com/Vova-Bob/SCLOC-Verse
