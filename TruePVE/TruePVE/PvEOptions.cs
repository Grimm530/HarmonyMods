using Newtonsoft.Json;

namespace TruePVE;

public class PvEOptions
{
	[JsonProperty(PropertyName = "Enable game server.pve (block PvP + reflect, demolish, tags)")]
	public bool EnableGamePvE = true;

	[JsonProperty(PropertyName = "PvE bullet damage multiplier (player -> NPC)")]
	public float PveBulletDamageMultiplier = 1f;

	[JsonProperty(PropertyName = "Protect sleeping players (block damage from other players)")]
	public bool ProtectSleepingPlayers = true;

	[JsonProperty(PropertyName = "Player auto turrets ignore players (target NPCs/animals only)")]
	public bool TurretsIgnorePlayers = true;

	[JsonProperty(PropertyName = "Static/monument auto turrets ignore players")]
	public bool StaticTurretsIgnorePlayers;

	[JsonProperty(PropertyName = "Safe zone NPC auto turrets ignore players")]
	public bool SafeZoneTurretsIgnorePlayers;

	[JsonProperty(PropertyName = "Turret ignore players debug logging")]
	public bool TurretIgnorePlayersDebug;
}
