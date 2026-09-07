using Newtonsoft.Json;

namespace TruePVE;

public class LootDefenderOptions
{
	[JsonProperty(PropertyName = "Enabled")]
	public bool Enabled = true;

	[JsonProperty(PropertyName = "Lock Bradley Crates")]
	public bool LockBradley = true;

	[JsonProperty(PropertyName = "Lock Patrol Heli Crates")]
	public bool LockHeli = true;

	[JsonProperty(PropertyName = "Lock NPC Corpses")]
	public bool LockNpc = true;

	[JsonProperty(PropertyName = "Lock Radius (meters)")]
	public float LockRadius = 25f;

	[JsonProperty(PropertyName = "Lock Duration (seconds, 0 = forever)")]
	public int LockSeconds = 900;

	[JsonProperty(PropertyName = "Group By Team (sum team damage)")]
	public bool GroupByTeam = true;

	[JsonProperty(PropertyName = "Allow Allies (Clan/Friends) Of Winners")]
	public bool AllowAllies = true;

	[JsonProperty(PropertyName = "Block Looting Only (don't block damage)")]
	public bool BlockLootingOnly = true;

	[JsonProperty(PropertyName = "Bradley - Damage Lock Threshold")]
	public float BradleyThreshold = 0.2f;

	[JsonProperty(PropertyName = "Bradley - Lock Time (seconds, 0 = forever)")]
	public int BradleyLockTime = 900;

	[JsonProperty(PropertyName = "Helicopter - Damage Lock Threshold")]
	public float HeliThreshold = 0.2f;

	[JsonProperty(PropertyName = "Helicopter - Lock Time (seconds, 0 = forever)")]
	public int HeliLockTime = 900;

	[JsonProperty(PropertyName = "NPC - Damage Lock Threshold")]
	public float NpcThreshold = 0.2f;

	[JsonProperty(PropertyName = "NPC - Lock Time (seconds, 0 = forever)")]
	public int NpcLockTime = 900;

	[JsonProperty(PropertyName = "Hackable Crates - Enabled")]
	public bool HackableEnabled = true;

	[JsonProperty(PropertyName = "Hackable Crates - Lock Time (seconds, 0 = forever)")]
	public int HackableLockTime = 900;

	[JsonProperty(PropertyName = "Hackable Crates - Block Timer Increase On Damage To Laptop")]
	public bool HackableBlockTimerIncrease = true;
}
