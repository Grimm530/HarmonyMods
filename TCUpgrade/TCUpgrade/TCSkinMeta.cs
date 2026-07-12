using System.Collections.Generic;

namespace TCUpgrade;

public static class TCSkinMeta
{
	public static readonly Dictionary<TCSkin, (string ShortName, string PrefabPath, string EffectPath, int ItemID, int SkinID)> Data = new Dictionary<TCSkin, (string, string, string, int, int)>
	{
		[TCSkin.Default] = ("cupboard.tool", "assets/prefabs/deployable/tool cupboard/cupboard.tool.deployed.prefab", "assets/prefabs/deployable/tool cupboard/effects/tool-cupboard-deploy.prefab", -97956382, 0),
		[TCSkin.Retro] = ("cupboard.tool.retro", "assets/prefabs/deployable/tool cupboard/retro/cupboard.tool.retro.deployed.prefab", "assets/prefabs/deployable/tool cupboard/retro/effects/tool-cupboard-retro-deploy.prefab", 1488606552, 10238),
		[TCSkin.Shockbyte] = ("cupboard.tool.shockbyte", "assets/prefabs/deployable/tool cupboard/shockbyte/cupboard.tool.shockbyte.deployed.prefab", "assets/prefabs/deployable/tool cupboard/effects/tool-cupboard-deploy.prefab", 1174957864, 10239)
	};
}
