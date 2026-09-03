# Balance Island for Windows

[简体中文](README.md) | **English**

> Documentation is available in Chinese and English. The application UI currently supports Simplified Chinese only.

A Windows taskbar monitor for AI API balances and usage. This repository is the Windows implementation based on Android [Balance Island v0.9.2](https://github.com/Noyorin0011/BalanceIsland/releases/tag/v0.9.2). The current application version is `0.3.0`.

## Features

- Windows 10 1809+ and Windows 11, built with C#, .NET 8, WPF, and Win32.
- Stays in the system tray after the main window closes; use the tray menu to reopen, refresh, toggle the island, or exit.
- Uses a transparent floating mode on Windows 11 and retains the compatible floating/taskbar-widget mode on Windows 10.
- Provides Widgets-adjacent, taskbar-centered, and pre-notification-area positions, plus compact, standard, large, and custom sizes.
- Edit mode enables dragging and eight-direction resizing; disabling it fixes the island and enables click-through.
- Rotates selected accounts every five seconds. Windows events drive immediate full-screen enter/exit handling, followed by an 80 ms verification.
- Supports System, Light, and Dark themes. Windows high-contrast mode always takes priority.
- Supports multiple accounts per provider, safe key suffixes, manual balances, warning thresholds, abnormal-change detection, and notifications.
- Cleans API keys and stores manually entered secrets in Windows Credential Manager. Full keys are never written to local JSON.
- Supports per-account refresh intervals from `1–1440` minutes; `0` uses the provider recommendation inherited from v0.9.2.
- Honors HTTP 429 `Retry-After` responses and uses exponential backoff up to 24 hours.
- Searches provider metadata and scans process, current-user, and machine environment variables.
- Automatic scans prompt only for new credentials; every import still requires explicit user selection.
- Supports a persistent silent-start setting and a one-time `--silent` argument. The tray icon, island, and background refresh continue while the main window stays hidden.
- Rotates accounts or aggregates accounts from the same provider, with five palettes and four customizable state colors.
- Uses important notifications on Windows 11, normal notifications on Windows 10, and tray-balloon fallback.
- Supports DeepSeek, OpenAI, OpenRouter, SiliconFlow, Moonshot, MiMo, Anthropic, Gemini, and xAI.

### Provider behavior

| Provider | Current behavior |
| --- | --- |
| DeepSeek | Official balance, topped-up balance, and promotional balance |
| OpenAI | `sk-admin-` queries organization cost/limits for the current month; other keys are validation-only |
| OpenRouter | Management keys query total limit, cumulative usage, and daily usage when available |
| SiliconFlow | Official `/user/info` total account balance |
| Moonshot | Falls back between the China CNY endpoint and international USD endpoint |
| MiMo | Validates regular keys, rejects Token Plan `tp-` keys, and uses a manually entered balance |
| Anthropic / Gemini / xAI | Validates against official model endpoints and uses a manually entered balance |

## Known limitations

- **Vertical taskbar support remains unstable:** on Windows 10, island positioning and styling may be inaccurate when the taskbar is docked left or right. The default horizontal taskbar works normally. See `docs/testing/v0.3.0-validation.md`.
- Changing Windows 11 taskbar alignment between left and centered may cause a short positioning delay.
- The application UI currently supports Simplified Chinese only. This English README does not add English UI localization.

## v0.3.0 guide

The labels below include their current Chinese UI text. Existing `state.json` files can be retained: missing fields receive safe defaults, and full API keys are neither written nor displayed.

### 1. Choose System, Light, or Dark theme

Open **Display & Appearance** (`显示与外观`) and choose Follow system (`跟随系统`), Light (`浅色`), or Dark (`深色`). Windows high-contrast mode always overrides this choice.

### 2. Configure state colors

Open **Island colors** (`浮岛配色`) and choose Classic, Mint, Sky, Coral, or Lime, or enter custom Normal, Error, Warning, and Critical colors. Only `#RRGGBB` and `#AARRGGBB` are accepted.

Severity order is Critical, Error, Warning, then Normal. A balance at or below the threshold is Critical; abnormal movement is Error; a balance above the threshold but no greater than `threshold × 1.15` is Warning.

### 3. Create display groups

- Rotation groups may mix providers and cycle through enabled members.
- Aggregation groups contain one provider and sum numeric fields only when currencies match.
- Validation-only providers show valid/error key counts instead of fabricated balances.

### 4. Scan environment variables

1. Open **Environment API** (`环境 API`) and optionally enable the startup scan prompt. It never creates accounts automatically.
2. Choose **Scan environment** (`扫描环境`). The app scans process, current-user, and machine variables.
3. Results are unselected by default. Ambiguous candidates such as generic `sk-` keys are fully masked and require an explicit provider choice.

Automatic startup scans prompt only for credentials not already represented by an account. No dialog appears when nothing is new. Manual scans still show the complete candidate list.

Within one provider, a second variable is ignored when it resolves to the same normalized key as an existing account. Comparison occurs only in memory; no key hash is persisted. If the original variable has been removed, the new variable may be prompted so the account can be relinked.

### 5. Configure silent startup

1. In **Display & Appearance**, enable **Silent startup (do not open the main window)** (`静默启动（不打开主窗口）`). It applies on the next launch.
2. Run `BalanceIsland.exe --silent` for a one-time silent launch. The argument is case-insensitive and does not modify the saved setting.
3. Silent startup still creates the tray icon and island and starts background refresh. A new environment credential sends a normal Windows notification. When the main window opens later, the app rescans and only then shows credentials that still exist.

### 6. Search providers

In **Accounts / API** (`账户 / API`), search by provider name, alias, environment-variable name, key prefix, or limitation keyword. The centralized registry supplies capabilities, default currency, refresh interval, matching rules, and limitations.

### 7. Configure Windows notifications

Open **Refresh & Notifications** (`刷新与通知`) to configure near-threshold, threshold-reached, and abnormal-change notifications independently. Windows 11 uses important notifications when supported; Windows 10 uses normal notifications. Native delivery failure falls back to a tray balloon.

Notifications fire when entering a warning or critical state, not repeatedly while staying there. Windows notification permissions, Focus Assist, user choices, and organization policy always have final control.

## Upgrade and rollback

- Upgrading from v0.2.1 supplies safe defaults, all three notification types enabled, and silent startup disabled.
- Migration recalculates legacy alert levels but does not itself send notifications.
- Back up `%LOCALAPPDATA%\BalanceIsland\state.json` before upgrading. Manually entered secrets remain in Windows Credential Manager; environment-account secrets remain in environment variables. Neither is copied into JSON.
- If the state file cannot be read, the app starts safely and does not overwrite the original until a later valid save.

## Manual validation

### `win11-24h2-vm`

- Verify themes, high-contrast priority, palettes, groups, and currency mismatch behavior.
- Verify process/user/machine scanning, opt-in import, duplicate prevention, and no prompt when nothing is new.
- Verify normal startup prompts for new credentials; silent startup and `--silent` hide the main window, notify only when something is new, and prompt after the window opens.
- Verify vertical centering, all four Start/Widgets layouts, Z-order recovery, notifications, and full-screen hide/restore.

### `win10-22h2-vm`

- Verify themes, colors, groups, environment scanning, normal notifications, tray fallback, compatible taskbar layout, and full-screen behavior.

## Relationship to Android v0.9.2

The Windows version does not yet claim one-to-one UI parity. Later work includes private ChatGPT/Codex subscription access, mixed subscription/API rotation, five UI languages, smooth long-text scrolling, full multi-monitor/taskbar-auto-hide validation, and signed MSIX/Release workflows.

Any future private ChatGPT/Codex integration must retain explicit risk confirmation, treat sessions as passwords, store only filtered usage fields, and allow automatic network access only from the experimental page.

## Build

Install the **.NET desktop development** workload in Visual Studio 2022 or install the .NET 8 SDK:

```powershell
dotnet restore BalanceIsland-Windows.sln
dotnet test BalanceIsland-Windows.sln -c Release --no-restore
dotnet build BalanceIsland-Windows.sln -c Release
dotnet run --project src/BalanceIsland.Windows/BalanceIsland.Windows.csproj
```

GitHub Actions runs solution tests, a Release build, and a `win-x64` publish, then uploads `BalanceIsland-Windows-v0.3.0-win-x64`.

## Data and security

- Manually entered API keys are stored in the current Windows user's Credential Manager under `BalanceIsland/<account-id>`.
- Environment-account keys remain in Windows environment variables and are read dynamically; they are not copied into Credential Manager or JSON.
- Account metadata, safe key suffixes, balance snapshots, and refresh state are stored in `%LOCALAPPDATA%\BalanceIsland\state.json`.
- Logs and errors must never expose full API keys.
- The app connects only to official provider HTTPS APIs and includes no proxy or certificate-bypass behavior.

## License

[GNU General Public License v3.0](LICENSE)
