# Fix NPC Performance Issue

## Problem
You have **2,283 ScarecrowNPCs** that are part of your map, causing severe performance issues (FPS dropped from 206 to 28).

## Solution: Enable AI Dormant System

### Step 1: Reload the NPCDiagnostic plugin
```
oxide.reload NPCDiagnostic
```

### Step 2: Check current AI dormant status
```
npc.check.dormant
```
OR use Rust's built-in command:
```
aimanager.ai_dormant
```

### Step 3: Enable AI Dormant (if not already enabled)
```
aimanager.ai_dormant true
```

### Step 4: Set wakeup range (optional, default is 50m)
```
aimanager.ai_to_player_distance_wakeup_range 50
```

## What This Does

When `ai_dormant` is enabled:
- NPCs outside the wakeup range (50m by default) will go dormant
- Dormant NPCs use **significantly less CPU** - they don't run ServerThink_Internal
- NPCs automatically wake up when players get within range
- This is the **recommended solution** for map-based NPCs

## Make It Permanent

Add these to your server startup config/autoexec file:
```
aimanager.ai_dormant true
aimanager.ai_to_player_distance_wakeup_range 50
```

## Temporary Cleanup (Optional)

If you need immediate relief, you can temporarily despawn some ScarecrowNPCs:
```
npc.cleanup.scarecrow 200
```
**Note:** They will respawn on server restart since they're part of the map.

## Expected Performance Improvement

With 2,283 NPCs and ai_dormant enabled:
- Only NPCs within 50m of players will be active
- If you have 10 players spread out, maybe 50-100 NPCs active at once
- This should restore your FPS to near-normal levels

