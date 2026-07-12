# ChatTranslator Harmony Mod

Standalone Harmony mod that translates chat messages to each player's language preference. **No Oxide required.** Works on servers without Oxide.

## Requirements

- **TranslationAPI** Harmony mod (must be loaded first)
- Rust Dedicated Server with Harmony loader

## Installation

1. Ensure **TranslationAPI.dll** is in `HarmonyMods/`
2. Copy **ChatTranslator.dll** to `HarmonyMods/`
3. Restart the server or run `harmony.load ChatTranslator`

## Configuration

Config: `HarmonyConfig/ChatTranslator.json` (created on first load if missing)

```json
{
  "Force default server language": false,
  "Log translated chat messages": false,
  "Show original and translation": false,
  "Translate message for sender": false,
  "Skip translation when sender and receiver use same language (saves API calls)": true,
  "Default server language code (e.g. en, es, de)": "en"
}
```

## Player Language

Players set their language with the chat command:

- `/lang` – Show current language
- `/lang en` – Set language to English
- `/lang es` – Set language to Spanish
- etc.

Languages are stored in `HarmonyConfig/ChatTranslator_languages.json`.

## Supported Channels

- **Global chat** – Translated per recipient
- **Team chat** – Translated per team member
- **Local chat** – Translated for nearby players
- **Card game chat** – Passed to original handler (no translation)

## Building

```powershell
cd .cursor\HarmonyMods\ChatTranslator
.\build.ps1
```

Requires Rust server Managed assemblies at `RustDedicated_Data\Managed\`.
