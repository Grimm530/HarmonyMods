# RustEdit IO – Oxide vs Game Assembly Cross-Reference

## Serialized types (Oxide.Ext.RustEdit.IO)

| File | Type | ProtoMember | Purpose |
|------|------|-------------|---------|
| **SerializedIOData.cs** | `SerializedIOData` | (1) `List<SerializedIOEntity> entities` | Root: list of all IO entities saved in map |
| **SerializedIOEntity.cs** | `SerializedIOEntity` | (1) fullPath, (2) position (VectorData), (3) inputs[], (4) outputs[], (5) accessLevel, (6) doorEffect, (7) timerLength, (8) frequency, (9) unlimitedAmmo, (10) peaceKeeper, (11) autoTurretWeapon, (12) branchAmount, (13) targetCounterNumber, (14) rcIdentifier, (15) counterPassthrough, (16) floors, (17) phoneName | One IO entity from editor |
| **SerializedConnectionData.cs** | `SerializedConnectionData` | (1) fullPath, (2) position (VectorData), (3) input, (4) connectedTo (slot index), (5) type | One input or output connection (target entity path/position + slot) |

Oxide uses **VectorData** (x,y,z); we use **IOVectorData** with the same layout for manual protobuf.

---

## How Oxide finds and connects IO

1. **Load IO data**  
   From map (e.g. `World.GetMap("rustedit_io")` or similar). Deserialized to `SerializedIOData` (protobuf).

2. **Get all IO entities in world**  
   `BaseNetworkable.serverEntities.Where(x => x is IOEntity).Cast<IOEntity>().ToList()`  
   Same as our: `BaseNetworkable.serverEntities.Where(x => x is IOEntity).Cast<IOEntity>().ToList()`.

3. **Match serialized entity → world entity**  
   Oxide: `P_0.PrefabName == P_1.fullPath` **and** `transform.position == VectorData.op_Implicit(P_1.position)` (exact match).  
   We: `PathMatch(PrefabName, fullPath)` (full path or last-segment key) and position with **tolerance 1f** (more forgiving).

4. **Match connection target**  
   Same idea: find `IOEntity` in the same list where `PrefabName == conn.fullPath` and `position == conn.position`.  
   We use the same `FindIOEntity(list, fullPath, position)`.

5. **Wire connections**  
   - **Outputs**: `sourceEntity.ConnectTo(targetEntity, outputIndex, inputIndex)` (game’s `IOEntity.ConnectTo`).  
   - **Inputs**: Oxide sets both sides (entity.inputs[i].connectedTo → source, source.outputs[slot].connectedTo → entity) then `Init()`.  
   We do the same: inputs we set both sides manually; outputs we use `ConnectTo`.

6. **Game API**  
   - **IOEntity.ConnectTo(IOEntity entity, int outputIndex, int inputIndex, ...)**  
     Sets: target’s input slot → this; this output slot → target; both `connectedTo`/`connectedToSlot`; then `MarkDirtyForceUpdateOutputs`, `SendNetworkUpdate`, etc.  
   - **IOEntity.Init()**  
     For each output, `connectedTo.Init()` and mirrors the connection on the other entity’s input (see IOEntity.cs ~887–902).  
   We call `entity.Init()` after wiring and then `MarkDirtyForceUpdateOutputs`, `SendNetworkUpdate`, `SendChangedToRoot`, `RefreshIndustrialPreventBuilding`.

---

## Oxide-only behaviours (encrypted/obfuscated)

These are **MonoBehaviour** components Oxide adds; we don’t add them, but we apply equivalent data where the game supports it.

| File | Role |
|------|------|
| **CardReaderMonitor.cs** | Gets `CardReader` in Awake; when a timer expires calls `ResetIOState()` and clears a “granted” flag. We set **CardReader.accessLevel** from `serializedIOEntity.accessLevel` in ApplyIOEntitySettings. |
| **AutoTurretManager.cs** | Sets **peacekeeper**, **unlimited ammo**, **weapon** (item by name) on AutoTurret; invokes `UpdateAttachedWeapon` / ammo refresh. We set **unlimitedAmmo**, **peaceKeeper**, **autoTurretWeapon** in ApplyIOEntitySettings. |
| **IOEntityToWheelSwitchConnection.cs** | Holds a list of **WheelSwitch** targets; when this IOEntity gets power (Flags), invokes `Powered()` on each; when power off, cancels invoke. We don’t add this component; wheel switches still get wired via normal output → input. |

---

## Game assembly types (Assembly-CSharp)

| File | Type | Relevance |
|------|------|------------|
| **IOEntity.cs** | IOEntity | inputs/outputs (IOSlot[]), IORef, ConnectTo(), Init(), Load/Save connections |
| **CardReader.cs** | CardReader : IOEntity | accessLevel, ResetIOState, GrantCard/CancelAccess |
| **AppIOEntity.cs** | AppIOEntity : IOEntity | Base for app-linked IO |
| **AutoTurret.cs** | AutoTurret : ContainerIOEntity | SetPeacekeepermode, AttachedWeapon, inventory |
| **ContainerIOEntity.cs** | ContainerIOEntity : IOEntity | inventory, IItemContainerEntity |
| **ClientIOLine.cs** | ClientIOLine | Client-side line renderer; ownerIOEnt, lineType |

---

## Map layer

- Game loads map layers via **World.GetMap(name)** → `World.Serialization.GetMap(name)?.data`.  
- Standard terrain: "height", "splat", "biome", "topology", "alpha", "water", "terrain".  
- Oxide expects IO data in a dedicated layer (e.g. **"rustedit_io"** or **"io"**).  
- Our **RustEditStandalone** tries `rustedit_io`, `io`, then every other map layer (except terrain); we pick the layer with the **most valid entities** (non-empty path or non–(0,0,0) position) and reject layers that only contain bogus (0,0,0) “catch pool” data.

---

## Summary

- **Schema**: Our `IOData.cs` (SerializedIOData, SerializedIOEntity, SerializedConnectionData, IOVectorData) matches Oxide’s ProtoContract layout for manual protobuf.  
- **List**: We use the same source of truth for “all IO in world” as Oxide: `serverEntities` filtered to `IOEntity`.  
- **Matching**: We match by path (full or key) + position (with tolerance); Oxide uses exact path + exact position.  
- **Wiring**: We mirror Oxide: both sides for inputs, `ConnectTo` for outputs, then `Init()` and network/root updates.  
- **Settings**: We apply CardReader, TimerSwitch, DoorManipulator, ElectricalBranch, RFReceiver/RFBroadcaster, PowerCounter, PressButton, AutoTurret, etc. in **ApplyIOEntitySettings**; we do not add CardReaderMonitor / AutoTurretManager / IOEntityToWheelSwitchConnection components.
