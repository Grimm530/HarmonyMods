using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace CustomGenerator.Shared;

public sealed class MapExtras
{
	public const string LayerName = "customgenerator";

	public const int CurrentVersion = 1;

	public int Version = 1;

	public List<IOEntityInfo> IO = new List<IOEntityInfo>();

	[JsonIgnore]
	public bool IsEmpty => IO.Count == 0;

	public static MapExtras FromBytes(byte[] data)
	{
		MapExtras mapExtras;
		if (data != null && data.Length != 0)
		{
			mapExtras = JsonConvert.DeserializeObject<MapExtras>(Encoding.UTF8.GetString(data));
			if (mapExtras == null)
			{
				return new MapExtras();
			}
		}
		else
		{
			mapExtras = new MapExtras();
		}
		return mapExtras;
	}

	public byte[] ToBytes()
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Expected O, but got Unknown
		return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject((object)this, (Formatting)0, new JsonSerializerSettings
		{
			NullValueHandling = (NullValueHandling)0
		}));
	}
}
