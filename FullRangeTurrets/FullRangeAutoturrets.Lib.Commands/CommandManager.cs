using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FullRangeAutoturrets.Lib.Logging;

namespace FullRangeAutoturrets.Lib.Commands;

public class CommandManager
{
	private List<Command> RegisteredCommands { get; set; }

	public CommandManager()
	{
		Reset();
	}

	public void Reset()
	{
		RegisteredCommands = new List<Command>();
	}

	private void RegisterCommand(string commandName, CommandHandlerAction action, CommandType type, CommandFlag flags)
	{
		commandName = commandName.ToLower();
		Command command = RegisteredCommands.Find((Command x) => x.Name == commandName && x.Type == type);
		if (command != null)
		{
			command.AddListener(action);
			return;
		}
		if (flags.HasFlag(CommandFlag.IncludePrefix))
		{
			commandName = "FullRangeAutoturrets".ToLower() + "." + commandName;
		}
		string text = ((type == CommandType.Console) ? "Console" : ((type == CommandType.Chat) ? "Chat" : "RCON"));
		LoggingManager.Log("Registering " + text + " command " + commandName);
		Command item = new Command(commandName, type, action, flags);
		RegisteredCommands.Add(item);
	}

	public void RegisterRCON(string commandName, CommandHandlerAction action, CommandFlag flags = CommandFlag.None)
	{
		RegisterCommand(commandName, action, CommandType.RCON, flags);
	}

	public void RegisterChat(string commandName, CommandHandlerAction action, CommandFlag flags = CommandFlag.None)
	{
		RegisterCommand(commandName, action, CommandType.Chat, flags);
	}

	public void RegisterConsole(string commandName, CommandHandlerAction action, CommandFlag flags = CommandFlag.None)
	{
		RegisterCommand(commandName, action, CommandType.Console, flags);
	}

	private KeyValuePair<string, string[]> ParseCommand(string commandString)
	{
		string[] array = commandString.Split(' ');
		string input = array[0].ToLower();
		input = Regex.Replace(input, "[^a-zA-Z0-9/_,.!]", "");
		input = Regex.Replace(input, "\\r\\n?|\\n", "");
		string[] array2;
		if (array.Length > 1)
		{
			array2 = new string[array.Length - 1];
			for (int j = 1; j < array.Length; j++)
				array2[j - 1] = array[j];
		}
		else
		{
			array2 = Array.Empty<string>();
		}
		for (int i = 0; i < array2.Length; i++)
		{
			array2[i] = Regex.Replace(array2[i], "[^a-zA-Z0-9/_,.!]", "");
		}
		return new KeyValuePair<string, string[]>(input, array2);
	}

	public bool Handler(ConsoleSystem.Option options, string strCommand, params object[] args)
	{
		KeyValuePair<string, string[]> keyValuePair = ParseCommand(strCommand);
		string commandName = keyValuePair.Key;
		object[] value = keyValuePair.Value;
		object[] array = value;
		CommandType cmdType = CommandType.RCON;
		if (options.Connection != null)
		{
			if (options.Connection != null && commandName == "chat.say" && array.Length != 0)
			{
				if (!array[0].ToString().StartsWith("/") && !array[0].ToString().StartsWith("!"))
				{
					return true;
				}
				commandName = array[0].ToString().Substring(1);
				object[] newArray = new object[array.Length - 1];
				for (int k = 1; k < array.Length; k++)
					newArray[k - 1] = array[k];
				array = newArray;
				cmdType = CommandType.Chat;
			}
			else
			{
				cmdType = CommandType.Console;
			}
		}
		try
		{
			Command command = null;
			for (int i = 0; i < RegisteredCommands.Count; i++)
			{
				var c = RegisteredCommands[i];
				if (c.Name == commandName && c.Type == cmdType) { command = c; break; }
			}
			if (command == null || !command.HasListeners())
			{
				return true;
			}
			BasePlayer sender = (options.Connection?.player as BasePlayer) ?? null;
			command.Execute(sender, array);
			return !command.Flags.HasFlag(CommandFlag.BlockExecution);
		}
		catch (Exception ex)
		{
			LoggingManager.Log("Error while executing command " + commandName + ": " + ex.Message);
			return true;
		}
	}
}
