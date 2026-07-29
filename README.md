<p align="center">
  <img src="src/Icon-Full-Art.png" alt="JASM+" width="200">
</p>

# JASM+ — Just Another Skin Manager Plus

**JASM+** is a community-driven continuation of [JASM](https://github.com/Jorixon/JASM) by Jorixon — a mod manager for games like Genshin Impact, Honkai Star Rail, Wuthering Waves, Zenless Zone Zero, and Arknights: Endfield (added by JASM+). Built with WinUI 3 and the Windows App SDK.

The original JASM is no longer actively maintained. JASM+ picks up where it left off: supporting new games, fixing bugs, and building features the community finds useful — while keeping the tool safe, transparent, and free of anything that could raise false alarms.

> **JASM has never contained malware.** That said, some of the original mechanisms — an admin-elevated side process and a separate auto-updater executable — can trigger overzealous antivirus heuristics. We've removed those entirely.

---

## Key Changes from Original JASM

### 🛡️ Elevator Service — Removed
The original "Elevator" was a separate process running with admin privileges that sent F10 keystrokes to games via Named Pipes. Harmless in intent, but an admin process injecting keystrokes is exactly what antivirus heuristics look for. **The cost-benefit was too low to justify keeping it.** Fully removed — project, service, UI, and all related code.

### 🔄 Auto-Updater — Replaced
The old updater was a standalone `.exe` that downloaded an archive, deleted files one by one, and copied new ones in place — another pattern that can look suspicious. JASM+ replaces it with an **in-app download + atomic directory swap**: download the release, extract alongside the current install, write a batch script, exit. The script swaps the folders and launches the new version. No separate binary, no file-deletion dance.

### 🌐 Community-Driven Game Resources
Instead of bundling character data and images inside the app, JASM+ loads game resources from a separate repository where anyone can open a pull request to add new characters, update assets, or contribute support for new games. New characters and fixes can arrive quickly — reviewed and merged by the community — without waiting for a full app release.

---

## Features

- Drag and drop mods directly into the app
- Auto-sort unsorted mods into their character folders
- Move mods between characters
- Launch 3DMigoto and/or your game from the app
- Real-time folder watching — mods appear instantly when added or removed
- Edit `merged.ini` keys
- Export all managed mods to a folder of your choice
- Batch operations: Enable All, Disable All, Clean Up disabled mod folders
- Custom commands: run your own scripts against mod folders from the context menu
- Multi-game support: Genshin Impact, Honkai Star Rail, Wuthering Waves, Zenless Zone Zero, Arknights: Endfield
- Localized in English & Español (fully validated), 中文, 日本語, Русский, and Português (Brasil) (experimental)

---

## New Features (Since Original JASM)

- **Arknights: Endfield support** — added from scratch; not available in the original JASM
- **Multi-game support** — Genshin Impact, Honkai Star Rail, Wuthering Waves, Zenless Zone Zero, Arknights: Endfield
- **Community-driven game resources** — characters and assets loaded from a separate repository; anyone can submit a PR to add or update them
- **Batch operations** — Enable All, Disable All, and Clean Up disabled mod folders from the character overview
- **Custom commands** — run your own scripts or tools against mod folders directly from the character context menu
- **Override folder settings** — per-mod control over folder behavior
- **Auto-close mod installer** — installer page closes automatically after a successful install
- **In-app updates** — download and install new releases without a separate updater binary
- **Localization** — English and Español (fully complete & validated); 中文, 日本語, Русский, and Português (Brasil) (experimental — may have gaps)

## Bug Fixes

- Custom commands with spaces in file paths now work correctly
- Settings and command configurations no longer lose data after updates
- Various popup and dialog crashes resolved

## What's Next

- Fixing open bugs — both ones I've found and ones you report
- More translations — Italian and Korean are next
- Bundled mods — link mods together so enabling one enables them all
- Random mod selection on launch — shuffle things up every time you start
- Full XXMI compatibility

## Download

**[Latest release](https://github.com/zurce/JASM/releases)** — also available on [GameBanana](https://gamebanana.com/tools/14574).

Run `JASM - Just Another Skin Manager.exe` from the `JASM/` folder. A shortcut is recommended.

---

## Requirements

- Windows 10 version 1809 or higher
- [.NET Desktop Runtime](https://aka.ms/dotnet-core-applaunch?missing_runtime=true&arch=x64&rid=win10-x64&apphost_version=9.0.0&gui=true)
- [Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)
- [Webp Image Extension](https://apps.microsoft.com/detail/9pg2dk419drg?hl=en-US&gl=US) (Windows 10 only)

The app will prompt you and provide links if any dependencies are missing.

---

## Was AI used for this project?

Yes. Several AI coding agents were used to help continue development. I'm a professional software developer and understand coding heuristics, patterns, and best practices — but .NET and WinUI 3 aren't my usual stack. The AI served as a bridge for the technology gap: generating code in a language and framework outside my day-to-day, while I provided the architectural direction and reviewed every line. It was used purely as a coding tool, not for any artistic or generative concept work. No human work was replaced by AI, because no human was working on this to begin with.

---

## Original Project

JASM+ continues the work of **JASM - Just Another Skin Manager** by [Jorixon](https://github.com/Jorixon).

- GitHub: [https://github.com/Jorixon/JASM](https://github.com/Jorixon/JASM)
- GameBanana: [https://gamebanana.com/tools/14574](https://gamebanana.com/tools/14574)

All credit for the foundation of this tool goes to Jorixon and the original contributors.
