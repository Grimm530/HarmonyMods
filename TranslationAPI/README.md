# TranslationAPI Harmony Mod

Standalone Harmony mod providing web-based translation (Google/Microsoft/Yandex). **No Oxide.** Used by ChatTranslator and Rustcord Harmony mods via direct call.

## Architecture

| Component | Purpose |
|-----------|---------|
| **TranslationAPI.dll** (Harmony mod) | Core translation logic, HTTP requests, config |

Other Harmony mods call `TranslationAPIMod.Translate(text, to, from, callback)` directly.

## Config

- `HarmonyConfig/TranslationAPI.json` (created on first load if missing)

```json
{
  "API key (if required)": "",
  "Translation service": "google"
}
```

- **google**: Free (no API key) or paid. Default.
- **bing** / **microsoft**: Requires API key
- **yandex**: Requires API key

## Loading

- Automatically at server startup (if DLL is in `HarmonyMods/`)
- Manual: `harmony.load TranslationAPI`

## Usage (ChatTranslator / Rustcord Harmony mods)

```csharp
TranslationAPIMod.Translate(message, "en", "auto", translated => {
    // use translated
});
```

## Dependencies

- No Oxide. Uses `System.Net.Http`, `UnityEngine` for coroutines.
