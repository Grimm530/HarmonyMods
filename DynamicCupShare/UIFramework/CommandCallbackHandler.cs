using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Oxide.Ext.Chaos.UIFramework;

public class CommandCallbackHandler
{
	private readonly Dictionary<string, KeyValuePair<ulong?, Action<ConsoleSystem.Arg>>> m_ConsoleCommands =
		new Dictionary<string, KeyValuePair<ulong?, Action<ConsoleSystem.Arg>>>();

	private readonly string m_Command;
	private readonly string m_CuiEndtestMarker;
	private readonly ConsoleSystem.Command m_CallbackCommand;
	private readonly StringBuilder m_IdentifierBuilder = new StringBuilder();
	private readonly string m_DisplayName;

	public string CommandFullName => m_Command;

	public CommandCallbackHandler(string pluginTitle, string cuiEndtestMarker = null)
	{
		if (string.IsNullOrEmpty(pluginTitle))
			pluginTitle = "adminmenu";

		string parent = pluginTitle.Replace(" ", "").ToLowerInvariant();
		m_DisplayName = pluginTitle;
		m_Command = parent + ".callback";
		m_CuiEndtestMarker = cuiEndtestMarker;

		m_CallbackCommand = new ConsoleSystem.Command
		{
			Name = "callback",
			Parent = parent,
			FullName = m_Command,
			ServerUser = true,
			ServerAdmin = true,
			Client = true,
			ClientInfo = false,
			Variable = false,
			Call = HandleCallback
		};

		try
		{
			ConsoleSystem.Index.Server.Dict[m_CallbackCommand.FullName] = m_CallbackCommand;
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[CommandCallbackHandler] Failed to register command " + m_Command + ": " + ex.Message);
		}
	}

	/// <summary>
	/// Accepts a host object with a Title property (e.g. AdminMenu), or a string prefix.
	/// </summary>
	public CommandCallbackHandler(object pluginOrTitle)
		: this(ResolveTitle(pluginOrTitle), null)
	{
	}

	public CommandCallbackHandler(object pluginOrTitle, string cuiEndtestMarker)
		: this(ResolveTitle(pluginOrTitle), cuiEndtestMarker)
	{
	}

	private static string ResolveTitle(object pluginOrTitle)
	{
		if (pluginOrTitle == null)
			return "adminmenu";

		if (pluginOrTitle is string s)
			return s;

		PropertyInfo titleProp = pluginOrTitle.GetType().GetProperty("Title", BindingFlags.Instance | BindingFlags.Public);
		if (titleProp != null && titleProp.PropertyType == typeof(string))
		{
			string title = titleProp.GetValue(pluginOrTitle, null) as string;
			if (!string.IsNullOrEmpty(title))
				return title;
		}

		return pluginOrTitle.GetType().Name;
	}

	public string RegisterCommand(Action<ConsoleSystem.Arg> callback, ulong? userId, string identifier = "")
	{
		if (string.IsNullOrEmpty(identifier))
		{
			identifier = callback.GetHashCode().ToString();
		}
		else
		{
			identifier = StripIdentifier(identifier);
			if (!userId.HasValue)
			{
				ulong parsed;
				if (ulong.TryParse(identifier.Split('.')[0], out parsed) && IsSteamId(parsed))
					userId = parsed;
			}
		}

		m_ConsoleCommands[identifier] = new KeyValuePair<ulong?, Action<ConsoleSystem.Arg>>(userId, callback);
		if (!string.IsNullOrEmpty(m_CuiEndtestMarker))
			return "cui.endtest " + m_CuiEndtestMarker + " " + identifier;
		return m_Command + " " + identifier;
	}

	public string RegisterSecureCommand(Action<ConsoleSystem.Arg> callback, ulong? userId, string identifier = "")
	{
		return RegisterCommand(callback, userId, identifier);
	}

	public void HandleCallback(ConsoleSystem.Arg arg)
	{
		if (arg == null) return;
		HandleCui(arg, arg.GetString(0));
	}

	/// <summary>
	/// Invoke a registered callback using the original <c>cui.endtest</c> arg (keeps the player connection).
	/// </summary>
	public void HandleCui(ConsoleSystem.Arg sourceArg, string identifier)
	{
		if (sourceArg == null || string.IsNullOrEmpty(identifier))
			return;

		identifier = StripIdentifier(identifier);
		KeyValuePair<ulong?, Action<ConsoleSystem.Arg>> entry;
		if (!m_ConsoleCommands.TryGetValue(identifier, out entry))
			return;

		BasePlayer player = sourceArg.Connection != null
			? sourceArg.Connection.player as BasePlayer
			: sourceArg.Player();
		if (entry.Key.HasValue)
		{
			if (player == null)
				return;

			ulong userId = player.userID.Get();
			if (entry.Key.Value != userId)
			{
				Debug.LogWarning(string.Format(
					"[CommandCallbackHandler] Player {0} ({1}) attempted unauthorized callback for {2}",
					player.displayName, userId, m_DisplayName));
				return;
			}
		}

		entry.Value?.Invoke(sourceArg);
	}

	public void Clear()
	{
		m_ConsoleCommands.Clear();
	}

	public void Unregister()
	{
		if (m_CallbackCommand == null)
			return;

		try
		{
			ConsoleSystem.Index.Server.Dict.Remove(m_CallbackCommand.FullName);
		}
		catch
		{
		}
	}

	private static bool IsSteamId(ulong id)
	{
		return id > 76561197960265728UL;
	}

	private string StripIdentifier(string s)
	{
		if (string.IsNullOrEmpty(s))
			return s;

		m_IdentifierBuilder.Clear();
		for (int i = 0; i < s.Length; i++)
		{
			char c = s[i];
			if ((c >= 'a' && c <= 'z') ||
			    (c >= 'A' && c <= 'Z') ||
			    (c >= '0' && c <= '9') ||
			    c == '.')
			{
				m_IdentifierBuilder.Append(c);
			}
		}

		return m_IdentifierBuilder.ToString();
	}
}
