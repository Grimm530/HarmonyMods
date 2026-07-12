using System.Text;
using StackManager.Helpers;

namespace StackManager.Utility;

public class Harmony : IHarmonyModHooks
{
	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine(string.Empty);
		stringBuilder.AppendLine(Properties.GetProduct() + ".dll v" + Properties.GetVersion());
		stringBuilder.AppendLine(Properties.GetCopyright() ?? "");
		stringBuilder.AppendLine(string.Empty);
		Log.None(stringBuilder.ToString());
		Stacker.Initialize();
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		Stacker.Kill();
		Log.Warning(Properties.GetProduct() + ".dll v" + Properties.GetVersion() + " unloaded.");
	}
}
