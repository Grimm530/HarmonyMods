# SignArtist (Harmony)

Oxide **SignArtist 1.4.52** port. Players with permission can download images from URLs (or the server filesystem) onto signs, frames, neon signs, and pumpkins.

## Commands

| Command | Permission | Description |
|---------|------------|-------------|
| `/sil <url> [raw]` | `signartist.url` | Load image from URL onto looked-at sign |
| `/silt <message> ...` | `signartist.text` | Render text onto a sign |
| `/sili` | `signartist.url` | Use held item / workshop skin icon |
| `/silrestore` | `signartist.restore` | Restore previous texture |

Also: `signartist.file`, `signartist.raw`, `signartist.ignorecd`, `signartist.ignoreowner`, `signartist.restoreall`.

## Config

`HarmonyConfig/SignArtist.json` — cooldown, max size, JPEG quality, Discord webhook logging (UnityWebRequest, no Oxide).

## Harmony patches

| Method | Kind |
|--------|------|
| `Chat.say` | prefix for `/sil` `/silt` `/sili` `/silrestore` |

HTTP downloads use `UnityWebRequest` as in the Oxide plugin. Image resize uses `System.Drawing`. No ImageLibrary.

Load order: **0Permissions → SignArtist**.
