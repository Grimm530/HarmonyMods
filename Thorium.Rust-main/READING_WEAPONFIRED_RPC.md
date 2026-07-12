# Reading OnWeaponFired (CLProject) RPC in Rust

## Overview

The `OnWeaponFired` RPC is called `CLProject` in the server code and is triggered when a player fires a weapon. This RPC contains all projectile information including starting position, velocity, seed, and projectile IDs.

## RPC Information

- **RPC Name**: `CLProject`
- **RPC ID**: `3168282921u` (0xBCD99999)
- **Direction**: Client → Server
- **Requirements**:
  - Must be from owner
  - Must be active item
  - Not mounted check: `includeMounted: false`

## Message Structure

The RPC message contains a `ProjectileShoot` protobuf structure.

### ProtoBuf.ProjectileShoot

```csharp
public class ProjectileShoot
{
    public int ammoType;                          // ItemDefinition.itemid of the ammo
    public List<Projectile> projectiles;          // List of projectiles fired (can be multiple for shotguns)
}
```

### ProtoBuf.ProjectileShoot.Projectile

```csharp
public class Projectile
{
    public int projectileID;       // Unique ID for this projectile (used to track it)
    public Vector3 startPos;       // Starting position (eye position)
    public Vector3 startVel;       // Starting velocity vector
    public int seed;               // Random seed for the projectile simulation
}
```

## Reading the RPC

### From Demo File

When reading from a demo file, the packet data will contain the serialized protobuf. Here's how to parse it:

```csharp
public void ParseCLProjectRPC(byte[] packetData)
{
    using (MemoryStream stream = new MemoryStream(packetData))
    using (BinaryReader reader = new BinaryReader(stream))
    {
        // First byte is the message type (should be 9 + 140 = 149 for RPCMessage)
        byte messageType = reader.ReadByte();
        
        if (messageType != 149) // MessageType.RPCMessage + 140
        {
            Console.WriteLine("Not an RPC message");
            return;
        }
        
        // RPC structure:
        // - uint32: RPC ID
        // - Protobuf: The actual RPC data
        
        uint rpcId = reader.ReadUInt32();
        
        if (rpcId != 3168282921u) // CLProject RPC ID
        {
            Console.WriteLine($"Not CLProject RPC (got {rpcId})");
            return;
        }
        
        // Read the rest as ProjectileShoot protobuf
        int remainingBytes = (int)(stream.Length - stream.Position);
        byte[] protobufData = reader.ReadBytes(remainingBytes);
        
        // Deserialize the ProjectileShoot protobuf
        using (BufferStream bufferStream = Pool.Get<BufferStream>().Initialize(protobufData))
        {
            ProjectileShoot projectileShoot = ProjectileShoot.Deserialize(bufferStream);
            
            Console.WriteLine($"Ammo Type: {projectileShoot.ammoType}");
            Console.WriteLine($"Projectile Count: {projectileShoot.projectiles.Count}");
            
            foreach (var projectile in projectileShoot.projectiles)
            {
                Console.WriteLine($"  Projectile ID: {projectile.projectileID}");
                Console.WriteLine($"  Start Position: {projectile.startPos}");
                Console.WriteLine($"  Start Velocity: {projectile.startVel}");
                Console.WriteLine($"  Seed: {projectile.seed}");
            }
        }
    }
}
```

### From Network Message (Live)

When intercepting live network traffic:

```csharp
public void OnRPCMessage(RPCMessage msg)
{
    if (msg.read == null)
        return;
    
    // Read the ProjectileShoot protobuf from the network message
    using (ProjectileShoot projectileShoot = msg.read.Proto<ProjectileShoot>())
    {
        BasePlayer player = msg.player;
        
        Console.WriteLine($"Player {player.displayName} fired weapon");
        Console.WriteLine($"Ammo Type: {projectileShoot.ammoType}");
        Console.WriteLine($"Projectile Count: {projectileShoot.projectiles.Count}");
        
        foreach (var projectile in projectileShoot.projectiles)
        {
            Console.WriteLine($"  Projectile ID: {projectile.projectileID}");
            Console.WriteLine($"  Start Position: {projectile.startPos}");
            Console.WriteLine($"  Start Velocity: {projectile.startVel}");
            Console.WriteLine($"  Seed: {projectile.seed}");
            
            // You can now track this projectile
            TrackProjectile(player, projectile);
        }
    }
}
```

## Server-Side Processing

The server processes the CLProject RPC as follows:

```csharp
public void CLProject(RPCMessage msg)
{
    BasePlayer player = msg.player;
    
    // Verify client attack is valid
    if (!VerifyClientAttack(player))
    {
        SendNetworkUpdate();
        return;
    }
    
    // Read the ProjectileShoot protobuf
    using ProjectileShoot projectileShoot = msg.read.Proto<ProjectileShoot>();
    
    // Validate ammo type
    ItemDefinition primaryMagazineAmmo = PrimaryMagazineAmmo;
    if (primaryMagazineAmmo.itemid != projectileShoot.ammoType)
    {
        AntiHack.Log(player, AntiHackType.ProjectileHack, "Ammo mismatch");
        return;
    }
    
    // Validate projectile count
    ItemModProjectile component = primaryMagazineAmmo.GetComponent<ItemModProjectile>();
    if (projectileShoot.projectiles.Count > component.numProjectiles)
    {
        AntiHack.Log(player, AntiHackType.ProjectileHack, "Count mismatch");
        return;
    }
    
    // Process each projectile
    foreach (ProjectileShoot.Projectile projectile in projectileShoot.projectiles)
    {
        // Validate projectile ID is unique
        if (player.HasFiredProjectile(projectile.projectileID))
        {
            AntiHack.Log(player, AntiHackType.ProjectileHack, "Duplicate ID");
            continue;
        }
        
        // Validate eye position
        if (!ValidateEyePos(player, projectile.startPos))
        {
            continue;
        }
        
        // Track the projectile
        player.NoteFiredProjectile(
            projectile.projectileID,
            projectile.startPos,
            projectile.startVel,
            this,
            primaryMagazineAmmo,
            projectileGroupId,
            positionOffset
        );
        
        // Create client-side effect
        CreateProjectileEffectClientside(
            component.GetOverrideProjectile(this).resourcePath,
            projectile.startPos,
            projectile.startVel,
            projectile.seed,
            msg.connection,
            IsSilenced()
        );
    }
}
```

## Protobuf Wire Format

The `ProjectileShoot` protobuf is serialized in the following format:

```
Field 1 (ammoType):
  Tag: 0x08 (field number 1, wire type 0 - varint)
  Value: varint encoded ammoType

Field 2 (projectiles) - repeated:
  Tag: 0x12 (field number 2, wire type 2 - length-delimited)
  Length: varint encoded length of Projectile message
  Value: Projectile protobuf data
  
  (repeated for each projectile)
```

### Projectile Protobuf Wire Format

Each `Projectile` in the list is serialized as:

```
Field 1 (projectileID):
  Tag: 0x08 (field number 1, wire type 0 - varint)
  Value: varint encoded projectileID

Field 2 (startPos) - Vector3:
  Tag: 0x12 (field number 2, wire type 2 - length-delimited)
  Length: 12 (3 floats × 4 bytes)
  Value: x (float32), y (float32), z (float32)

Field 3 (startVel) - Vector3:
  Tag: 0x1A (field number 3, wire type 2 - length-delimited)
  Length: 12 (3 floats × 4 bytes)
  Value: x (float32), y (float32), z (float32)

Field 4 (seed):
  Tag: 0x20 (field number 4, wire type 0 - varint)
  Value: varint encoded seed
```

## Manual Protobuf Parsing (Without Libraries)

If you need to parse without using the Rust protobuf library:

```csharp
public class ManualProjectileShootParser
{
    public static ProjectileShootData Parse(byte[] data)
    {
        int position = 0;
        ProjectileShootData result = new ProjectileShootData();
        result.projectiles = new List<ProjectileData>();
        
        while (position < data.Length)
        {
            // Read tag (field number + wire type)
            byte tag = data[position++];
            int fieldNumber = tag >> 3;
            int wireType = tag & 0x07;
            
            if (fieldNumber == 1) // ammoType
            {
                result.ammoType = ReadVarint(data, ref position);
            }
            else if (fieldNumber == 2) // projectiles
            {
                int length = ReadVarint(data, ref position);
                byte[] projectileData = new byte[length];
                Array.Copy(data, position, projectileData, 0, length);
                position += length;
                
                ProjectileData projectile = ParseProjectile(projectileData);
                result.projectiles.Add(projectile);
            }
            else
            {
                // Skip unknown field
                SkipField(data, ref position, wireType);
            }
        }
        
        return result;
    }
    
    private static ProjectileData ParseProjectile(byte[] data)
    {
        int position = 0;
        ProjectileData result = new ProjectileData();
        
        while (position < data.Length)
        {
            byte tag = data[position++];
            int fieldNumber = tag >> 3;
            int wireType = tag & 0x07;
            
            switch (fieldNumber)
            {
                case 1: // projectileID
                    result.projectileID = ReadVarint(data, ref position);
                    break;
                    
                case 2: // startPos
                    int posLength = ReadVarint(data, ref position);
                    result.startPos = ReadVector3(data, ref position);
                    break;
                    
                case 3: // startVel
                    int velLength = ReadVarint(data, ref position);
                    result.startVel = ReadVector3(data, ref position);
                    break;
                    
                case 4: // seed
                    result.seed = ReadVarint(data, ref position);
                    break;
                    
                default:
                    SkipField(data, ref position, wireType);
                    break;
            }
        }
        
        return result;
    }
    
    private static int ReadVarint(byte[] data, ref int position)
    {
        int result = 0;
        int shift = 0;
        
        while (true)
        {
            byte b = data[position++];
            result |= (b & 0x7F) << shift;
            
            if ((b & 0x80) == 0)
                break;
                
            shift += 7;
        }
        
        return result;
    }
    
    private static Vector3 ReadVector3(byte[] data, ref int position)
    {
        Vector3 result = new Vector3();
        result.x = BitConverter.ToSingle(data, position);
        position += 4;
        result.y = BitConverter.ToSingle(data, position);
        position += 4;
        result.z = BitConverter.ToSingle(data, position);
        position += 4;
        return result;
    }
    
    private static void SkipField(byte[] data, ref int position, int wireType)
    {
        switch (wireType)
        {
            case 0: // Varint
                ReadVarint(data, ref position);
                break;
            case 1: // 64-bit
                position += 8;
                break;
            case 2: // Length-delimited
                int length = ReadVarint(data, ref position);
                position += length;
                break;
            case 5: // 32-bit
                position += 4;
                break;
        }
    }
}

public class ProjectileShootData
{
    public int ammoType;
    public List<ProjectileData> projectiles;
}

public class ProjectileData
{
    public int projectileID;
    public Vector3 startPos;
    public Vector3 startVel;
    public int seed;
}
```

## Anticheat Validation

When implementing anticheat, you should validate:

### 1. Projectile ID Uniqueness
```csharp
if (player.HasFiredProjectile(projectile.projectileID))
{
    // Player tried to reuse a projectile ID
    Flag_DuplicateProjectileID(player);
}
```

### 2. Eye Position Validation
```csharp
bool ValidateEyePos(BasePlayer player, Vector3 startPos)
{
    Vector3 eyePos = player.eyes.position;
    float distance = Vector3.Distance(eyePos, startPos);
    
    // Allow small tolerance for network lag
    if (distance > 0.5f)
    {
        Flag_InvalidStartPosition(player);
        return false;
    }
    
    return true;
}
```

### 3. Ammo Type Validation
```csharp
if (projectileShoot.ammoType != weapon.PrimaryMagazineAmmo.itemid)
{
    Flag_AmmoTypeMismatch(player);
}
```

### 4. Projectile Count Validation
```csharp
ItemModProjectile ammoMod = ammo.GetComponent<ItemModProjectile>();
if (projectileShoot.projectiles.Count > ammoMod.numProjectiles)
{
    Flag_TooManyProjectiles(player);
}
```

### 5. Velocity Validation
```csharp
float expectedSpeed = ammoMod.projectileVelocity * weapon.projectileVelocityScale;
float actualSpeed = projectile.startVel.magnitude;

if (actualSpeed > expectedSpeed * 1.1f) // 10% tolerance
{
    Flag_InvalidVelocity(player);
}
```

### 6. Firing Rate Validation
```csharp
float timeSinceLastShot = Time.time - player.lastShotTime;
float minTimeBetweenShots = 1f / weapon.repeatDelay;

if (timeSinceLastShot < minTimeBetweenShots)
{
    Flag_RapidFire(player);
}
```

## Usage in Demo Analysis

When analyzing demo files for suspicious activity:

```csharp
public void AnalyzeDemoForAimbot(string demoFile)
{
    Reader reader = Reader.FromFile(demoFile);
    
    while (!reader.IsFinished)
    {
        Packet packet = reader.ReadPacket();
        if (!packet.isValid)
            break;
        
        using (MemoryStream ms = new MemoryStream(packet.Data))
        using (BinaryReader br = new BinaryReader(ms))
        {
            byte messageType = br.ReadByte();
            
            // Check for RPC messages
            if (messageType == 149) // RPCMessage
            {
                uint rpcId = br.ReadUInt32();
                
                // Check for CLProject (weapon fired)
                if (rpcId == 3168282921u)
                {
                    int remaining = (int)(ms.Length - ms.Position);
                    byte[] protobufData = br.ReadBytes(remaining);
                    
                    ProjectileShootData shot = ManualProjectileShootParser.Parse(protobufData);
                    
                    // Analyze shot direction vs nearby players
                    AnalyzeShotForAimbot(packet.Time, shot);
                }
            }
        }
    }
}
```

## Common Projectile Seeds

The seed value is used for deterministic random number generation in projectile spread/recoil:

- **Negative seeds**: Often indicate client-side modifications
- **Sequential seeds**: Normal behavior (seed increments each shot)
- **Duplicate seeds**: Suspicious, could indicate replay attacks

## Notes

- Multiple projectiles in one RPC are normal for shotguns (up to 12-20 pellets)
- Each projectile gets a unique ID that is tracked throughout its lifetime
- The seed determines the randomness of spread and trajectory
- StartPos should always be very close to player eye position
- StartVel magnitude should match weapon/ammo velocity stats
- Projectile IDs are typically generated client-side and must be unique per player

## Related RPCs

After CLProject, you may see:

- **PlayerProjectileUpdate** (RPC): Client updates projectile position mid-flight
- **PlayerProjectileAttack** (RPC): Projectile hit something
- **PlayerProjectileRicochet** (RPC): Projectile ricocheted

All of these use the same projectileID to correlate events.

## References

- Assembly-CSharp.dll: `BaseProjectile.CLProject()` method
- Rust.Data.dll: `ProtoBuf.ProjectileShoot` and `ProtoBuf.ProjectileShoot.Projectile`
- Demo format: See DEMO_RECORDING_FORMAT.md for general packet structure
