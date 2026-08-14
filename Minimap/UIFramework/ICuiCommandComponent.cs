using System;

namespace Oxide.Ext.Chaos.UIFramework;

public interface ICuiCommandComponent
{
	string Command { get; set; }

	void SetCommand(CommandCallbackHandler commandCallbackHandler, Action<ConsoleSystem.Arg> callback, string identifier);
}
