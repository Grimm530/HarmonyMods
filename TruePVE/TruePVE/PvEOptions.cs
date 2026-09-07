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
}
