# Chill With You AI Companion

An opt-in, personal BepInEx 5 mod for the Windows x64 Mono build of *Chill with You: Lo-Fi Story*.

The mod never patches `Assembly-CSharp.dll` on disk and keeps its encrypted conversation data outside the game's save directory.

## Build

Run `build.ps1`. The script expects the paths configured in `Directory.Build.props` and produces `dist/AICompanion/BepInEx/plugins/AICompanion`.

## Install

Run `install.ps1`, then start the game once. Configure an HTTPS Chat Completions-compatible endpoint, model, and API key in the in-game AI panel.

Version 1.1 uses the game's story-dialogue visual resources for a bottom-screen conversation overlay. The toolbar speech-bubble button opens the overlay; history and settings remain available from its compact controls.

## Remove

Run `uninstall.ps1`. It removes only the AI Companion plugin and leaves BepInEx in place so other mods are not affected. Encrypted history is retained unless `-RemoveEncryptedData` is supplied.
