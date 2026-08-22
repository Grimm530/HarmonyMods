# SortButton is superseded by LootQoL. Do not copy SortButton.dll into HarmonyMods.
# Loading both creates two UISortButton overlays; DestroyUI only removes one, so the
# button (especially on the tool cupboard) stays on screen after loot closes.

Write-Host "SortButton is superseded. Sort lives in LootQoL — not deploying SortButton.dll." -ForegroundColor Yellow
Write-Host "Build LootQoL instead: ..\LootQoL\build.ps1" -ForegroundColor Cyan
exit 0
