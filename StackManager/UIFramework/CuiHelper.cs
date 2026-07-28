using Oxide.Ext.Chaos.UIFramework;

namespace Oxide.Game.Rust.Cui;

public static class CuiHelper
{
	public static string GetGuid() => System.Guid.NewGuid().ToString("N");

	public static void DestroyUi(BasePlayer player, string name)
	{
		if (player == null || string.IsNullOrEmpty(name)) return;
		ChaosUI.Destroy(player, name);
	}
}
