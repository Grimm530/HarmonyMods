using System.Collections.Generic;

namespace TCUpgrade;

public static class WallPrefabs
{
	public static readonly Dictionary<int, string> Walls = new Dictionary<int, string>
	{
		[0] = "assets/prefabs/building/wall.external.high.wood/wall.external.high.wood.prefab",
		[10302] = "assets/prefabs/building/wall.external.high.legacy/wall.external.high.legacy.prefab",
		[1] = "assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab",
		[10304] = "assets/prefabs/building/wall.external.high.adobe/wall.external.high.adobe.prefab",
		[2] = "assets/prefabs/misc/xmas/icewalls/wall.external.high.ice.prefab"
	};

	public static readonly Dictionary<int, string> Gates = new Dictionary<int, string>
	{
		[0] = "assets/prefabs/building/gates.external.high/gates.external.high.wood/gates.external.high.wood.prefab",
		[10302] = "assets/prefabs/building/gates.external.high.legacy/gates.external.high.legacy.prefab",
		[1] = "assets/prefabs/building/gates.external.high/gates.external.high.stone/gates.external.high.stone.prefab",
		[10304] = "assets/prefabs/building/gates.external.high.adobe/gates.external.high.adobe.prefab",
		[2] = "assets/prefabs/building/gates.external.high/gates.external.high.stone/gates.external.high.stone.prefab"
	};
}
