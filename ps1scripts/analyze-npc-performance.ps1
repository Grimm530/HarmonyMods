# Quick NPC Performance Analysis
# Based on metrics report data

$npcCount = 800
$lookbackMinutes = 60

# NPC-related hook times from report (in seconds)
$baseAIBrainTickMovement = 55.04
$baseNpcTickAi = 12.76
$npcPlayerTickMovement = 5.89
$npcShopKeeperGreeting = 1.23

$totalNpcTime = $baseAIBrainTickMovement + $baseNpcTickAi + $npcPlayerTickMovement + $npcShopKeeperGreeting

Write-Host "=== NPC Performance Analysis (800 NPCs) ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "Total NPC Processing Time (60 min):" -ForegroundColor Yellow
Write-Host "  BaseAIBrain::TickMovement: $baseAIBrainTickMovement s" -ForegroundColor White
Write-Host "  BaseNpc::TickAi: $baseNpcTickAi s" -ForegroundColor White
Write-Host "  NPCPlayer::TickMovement: $npcPlayerTickMovement s" -ForegroundColor White
Write-Host "  NPCShopKeeper::Greeting: $npcShopKeeperGreeting s" -ForegroundColor White
Write-Host "  TOTAL: $totalNpcTime s ($([math]::Round($totalNpcTime / 60, 2)) min)" -ForegroundColor Green
Write-Host ""

# Per-NPC calculations
$totalNpcTimeMs = $totalNpcTime * 1000
$perNpcPerMinute = $totalNpcTimeMs / $npcCount / $lookbackMinutes
$perNpcPerSecond = $perNpcPerMinute / 60

Write-Host "Per-NPC Metrics:" -ForegroundColor Yellow
Write-Host "  Per NPC per minute: $([math]::Round($perNpcPerMinute, 3)) ms" -ForegroundColor White
Write-Host "  Per NPC per second: $([math]::Round($perNpcPerSecond, 4)) ms" -ForegroundColor White
Write-Host ""

# Assuming ~20 ticks/second (50ms per tick)
$ticksPerSecond = 20
$ticksPerMinute = $ticksPerSecond * 60
$totalTicks = $ticksPerMinute * $lookbackMinutes
$perNpcPerTick = $totalNpcTimeMs / ($npcCount * $totalTicks)

Write-Host "Per-Tick Metrics (assuming 20 ticks/sec):" -ForegroundColor Yellow
Write-Host "  Total ticks in 60 min: $totalTicks" -ForegroundColor Gray
Write-Host "  Per NPC per tick: $([math]::Round($perNpcPerTick, 4)) ms" -ForegroundColor White
Write-Host ""

# Server performance context
$avgFPS = 215.06
$avgFrameTime = 4.68
$worstFrameTime = 8.08

Write-Host "Server Performance Context:" -ForegroundColor Yellow
Write-Host "  Average FPS: $avgFPS" -ForegroundColor White
Write-Host "  Average Frame Time: $avgFrameTime ms" -ForegroundColor White
Write-Host "  Worst Frame Time: $worstFrameTime ms" -ForegroundColor White
Write-Host ""

# Calculate NPC overhead as % of frame time
# Total NPC time over 60 minutes = 74.92 seconds
# Per second of real time: 74.92s / 3600s = 0.0208s = 20.8ms per second
$totalSeconds = $lookbackMinutes * 60
$npcTimePerSecondMs = ($totalNpcTime * 1000) / $totalSeconds
$frameBudgetPerSecond = 1000  # One second = 1000ms
$npcOverheadPercent = ($npcTimePerSecondMs / $frameBudgetPerSecond) * 100

Write-Host "NPC Overhead Analysis:" -ForegroundColor Yellow
Write-Host "  NPC processing per second: $([math]::Round($npcTimePerSecondMs, 2)) ms" -ForegroundColor White
Write-Host "  Frame budget per second: 1000 ms (1 second)" -ForegroundColor White
Write-Host "  NPC overhead: $([math]::Round($npcOverheadPercent, 2))% of CPU time" -ForegroundColor White
Write-Host ""

# Assessment
Write-Host "=== Assessment ===" -ForegroundColor Cyan
Write-Host ""

if ($perNpcPerTick -lt 0.01) {
    Write-Host "✓ EXCELLENT: < 0.01ms per NPC per tick" -ForegroundColor Green
} elseif ($perNpcPerTick -lt 0.05) {
    Write-Host "✓ GOOD: < 0.05ms per NPC per tick" -ForegroundColor Green
} elseif ($perNpcPerTick -lt 0.1) {
    Write-Host "⚠ ACCEPTABLE: < 0.1ms per NPC per tick" -ForegroundColor Yellow
} else {
    Write-Host "✗ CONCERNING: > 0.1ms per NPC per tick" -ForegroundColor Red
}

Write-Host ""
if ($avgFPS -gt 200) {
    Write-Host "✓ EXCELLENT: Server FPS > 200" -ForegroundColor Green
} elseif ($avgFPS -gt 100) {
    Write-Host "✓ GOOD: Server FPS > 100" -ForegroundColor Green
} elseif ($avgFPS -gt 60) {
    Write-Host "⚠ ACCEPTABLE: Server FPS > 60" -ForegroundColor Yellow
} else {
    Write-Host "✗ CONCERNING: Server FPS < 60" -ForegroundColor Red
}

Write-Host ""
if ($npcOverheadPercent -lt 5) {
    Write-Host "✓ EXCELLENT: NPC overhead < 5% of frame budget" -ForegroundColor Green
} elseif ($npcOverheadPercent -lt 10) {
    Write-Host "✓ GOOD: NPC overhead < 10% of frame budget" -ForegroundColor Green
} elseif ($npcOverheadPercent -lt 20) {
    Write-Host "⚠ ACCEPTABLE: NPC overhead < 20% of frame budget" -ForegroundColor Yellow
} else {
    Write-Host "✗ CONCERNING: NPC overhead > 20% of frame budget" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Breakdown by Component ===" -ForegroundColor Cyan
Write-Host ""

$components = @(
    @{ Name = "BaseAIBrain::TickMovement"; Time = $baseAIBrainTickMovement },
    @{ Name = "BaseNpc::TickAi"; Time = $baseNpcTickAi },
    @{ Name = "NPCPlayer::TickMovement"; Time = $npcPlayerTickMovement },
    @{ Name = "NPCShopKeeper::Greeting"; Time = $npcShopKeeperGreeting }
)

foreach ($comp in $components) {
    $pct = ($comp.Time / $totalNpcTime) * 100
    $perNpc = ($comp.Time * 1000) / $npcCount / $lookbackMinutes
    Write-Host "$($comp.Name):" -ForegroundColor Yellow
    Write-Host "  Total: $($comp.Time) s ($pct% of NPC time)" -ForegroundColor Gray
    Write-Host "  Per NPC/min: $([math]::Round($perNpc, 3)) ms" -ForegroundColor Gray
    Write-Host ""
}
