namespace AlphaLoot.Harmony;

public class SkinEntry
{
	public int Weight;

	public ulong SkinID;

	public SkinEntry()
	{
	}

	public SkinEntry(ulong skinId, int weight = 1)
	{
		SkinID = skinId;
		Weight = weight;
	}
}
