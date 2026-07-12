using System;
using System.Collections.Generic;
using Facepunch;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace RecyclerSpeed;

/// <summary>
/// Handles /recyclerUI command and move-mode UI with arrow buttons to position overlay.
/// Mirrors Radar's move flow: arrows adjust anchormin/anchormax, Save writes to config.
/// </summary>
public class RecyclerSpeedMod
{
	public static RecyclerSpeedMod Instance { get; private set; }

	internal const string MovePanel = "RecyclerSpeed_Move";
	private const string PanelMaterial = "assets/content/ui/namefontmaterial.mat";
	private const string PanelSprite = "assets/content/ui/ui.background.tile.psd";
	private const float Step = 0.005f;

	private static ConsoleSystem.Command _cmd;
	internal readonly Dictionary<ulong, RecyclerSpeedUIState> PlayerStates = new Dictionary<ulong, RecyclerSpeedUIState>();

	public void OnLoaded(OnHarmonyModLoadedArgs args)
	{
		Instance = this;
		try
		{
			_cmd = new ConsoleSystem.Command
			{
				Name = "RECYCLER_SPEED_CMD",
				FullName = "global.RECYCLER_SPEED_CMD",
				Variable = true,
				ServerAdmin = false,
				Replicated = true,
				Call = HandleCmd
			};
			ConsoleSystem.Index.Server.Dict["global.RECYCLER_SPEED_CMD"] = _cmd;
			if (ConsoleSystem.Index.Server.GlobalDict != null)
				ConsoleSystem.Index.Server.GlobalDict["RECYCLER_SPEED_CMD"] = _cmd;
		}
		catch (Exception ex)
		{
			UnityEngine.Debug.LogWarning("[RecyclerSpeed] Command registration failed: " + ex.Message);
		}
	}

	private static void HandleCmd(ConsoleSystem.Arg arg)
	{
		var player = arg.Player();
		if (player == null) return;
		var mod = Instance;
		if (mod == null) return;
		mod.HandleCuiCommand(player, ToStringArray(arg.Args));
	}

	private static string[] ToStringArray(StringView[] args)
	{
		if (args == null || args.Length == 0) return Array.Empty<string>();

		var result = new string[args.Length];
		for (int i = 0; i < args.Length; i++)
			result[i] = args[i].ToString();
		return result;
	}

	public void OnUnloaded(OnHarmonyModUnloadedArgs args)
	{
		foreach (var kv in PlayerStates)
		{
			var p = BasePlayer.FindByID(kv.Key);
			if (p != null)
			{
				RecyclerSpeedUI.DestroyOverlay(p);
				DestroyMoveUI(p);
			}
		}
		PlayerStates.Clear();
		try
		{
			if (_cmd != null)
			{
				ConsoleSystem.Index.Server.Dict?.Remove("global.RECYCLER_SPEED_CMD");
				ConsoleSystem.Index.Server.GlobalDict?.Remove("RECYCLER_SPEED_CMD");
			}
		}
		catch { }
		Instance = null;
	}

	internal bool OnChatSay(BasePlayer player, string message)
	{
		if (player == null) return false;
		var msg = message?.Trim();
		if (string.IsNullOrEmpty(msg)) return false;
		if (msg.StartsWith("/")) msg = msg.Substring(1).Trim();
		if (!msg.Equals("recyclerUI", StringComparison.OrdinalIgnoreCase)) return false;

		if (!player.IsAdmin && !player.IsDeveloper)
		{
			SendMessage(player, "RecyclerSpeed UI requires admin.");
			return true;
		}

		ToggleMoveMode(player);
		return true;
	}

	internal bool HandleCuiCommand(BasePlayer player, string[] args)
	{
		if (player == null || args == null || args.Length < 1) return false;
		string action;
		if (args.Length >= 2 && string.Equals(args[0]?.ToString(), "RECYCLER_SPEED", StringComparison.OrdinalIgnoreCase))
			action = args[1]?.ToString() ?? "";
		else if (args.Length >= 1)
			action = args[0]?.ToString() ?? "";
		else
			return false;

		if (!player.IsAdmin && !player.IsDeveloper) return false;

		if (action == "TOGGLE_MOVE")
		{
			ToggleMoveModeFromClick(player);
			return true;
		}

		if (action == "CLOSE")
		{
			var state = GetOrCreateState(player);
			state.MoveModeActive = false;
			DestroyMoveUI(player);
			return true;
		}

		if (action == "MOVE_LEFT" || action == "MOVE_RIGHT" || action == "MOVE_UP" || action == "MOVE_DOWN")
		{
			var state = GetOrCreateState(player);
			ParseAnchors(state.UiAnchorMin, state.UiAnchorMax, out float minX, out float minY, out float maxX, out float maxY);
			float w = maxX - minX, h = maxY - minY;
			switch (action)
			{
				case "MOVE_LEFT": minX = Mathf.Max(0, minX - Step); maxX = minX + w; break;
				case "MOVE_RIGHT": maxX = Mathf.Min(1, maxX + Step); minX = maxX - w; break;
				case "MOVE_UP": maxY = Mathf.Min(1, maxY + Step); minY = maxY - h; break;
				case "MOVE_DOWN": minY = Mathf.Max(0, minY - Step); maxY = minY + h; break;
			}
			state.UiAnchorMin = $"{minX:F3} {minY:F3}";
			state.UiAnchorMax = $"{maxX:F3} {maxY:F3}";
			RefreshOverlayAndMovePanel(player);
			return true;
		}

		if (action == "SAVE")
		{
			var state = GetOrCreateState(player);
			HarmonyConfig.LoadConfig();
			if (HarmonyConfig.Config != null)
			{
				HarmonyConfig.Config.OverlayAnchormin = state.UiAnchorMin;
				HarmonyConfig.Config.OverlayAnchormax = state.UiAnchorMax;
				HarmonyConfig.SaveConfig();
				SendMessage(player, $"Position saved: {state.UiAnchorMin} / {state.UiAnchorMax}");
			}
			return true;
		}

		return false;
	}

	private static void ParseAnchors(string amin, string amax, out float minX, out float minY, out float maxX, out float maxY)
	{
		var a = amin?.Split(' ') ?? new[] { "0.841", "0.386" };
		var b = amax?.Split(' ') ?? new[] { "0.960", "0.415" };
		float.TryParse(a.Length > 0 ? a[0] : "0.841", out minX);
		float.TryParse(a.Length > 1 ? a[1] : "0.386", out minY);
		float.TryParse(b.Length > 0 ? b[0] : "0.960", out maxX);
		float.TryParse(b.Length > 1 ? b[1] : "0.415", out maxY);
	}

	/// <summary>
	/// Toggle from click on overlay (when recycler is open). No chat needed.
	/// Like Radar MOVE_TOGGLE: only show/hide move panel, never recreate the overlay.
	/// </summary>
	private void ToggleMoveModeFromClick(BasePlayer player)
	{
		var state = GetOrCreateState(player);
		state.MoveModeActive = !state.MoveModeActive;

		if (state.MoveModeActive)
		{
			HarmonyConfig.LoadConfig();
			if (HarmonyConfig.Config != null)
			{
				state.UiAnchorMin = HarmonyConfig.Config.OverlayAnchormin ?? "0.841 0.386";
				state.UiAnchorMax = HarmonyConfig.Config.OverlayAnchormax ?? "0.960 0.415";
			}
			DestroyMoveUI(player);
			SendMovePanel(player);
		}
		else
		{
			DestroyMoveUI(player);
		}
	}

	private void ToggleMoveMode(BasePlayer player)
	{
		var state = GetOrCreateState(player);
		state.MoveModeActive = !state.MoveModeActive;

		if (state.MoveModeActive)
		{
			HarmonyConfig.LoadConfig();
			if (HarmonyConfig.Config != null)
			{
				state.UiAnchorMin = HarmonyConfig.Config.OverlayAnchormin ?? "0.841 0.386";
				state.UiAnchorMax = HarmonyConfig.Config.OverlayAnchormax ?? "0.960 0.415";
			}
			SendMessage(player, "Move mode ON. Use arrows to position, Save to write config, Close when done.");
			RefreshMoveUI(player);
		}
		else
		{
			DestroyMoveUI(player);
			RecyclerSpeedUI.DestroyOverlay(player);
			SendMessage(player, "Move mode OFF.");
		}
	}

	/// <summary>
	/// When arrow clicked: move the existing overlay. Destroy first (Radar RefreshUI pattern), then resend overlay + move panel.
	/// </summary>
	private void RefreshOverlayAndMovePanel(BasePlayer player)
	{
		RecyclerSpeedUI.DestroyOverlay(player);
		RecyclerSpeedUI.RefreshOverlayFromRecycler(player);
		SendMovePanel(player);
	}

	internal RecyclerSpeedUIState GetOrCreateState(BasePlayer player)
	{
		if (player == null) return null;
		if (!PlayerStates.TryGetValue(player.userID, out var state))
		{
			state = new RecyclerSpeedUIState();
			PlayerStates[player.userID] = state;
		}
		return state;
	}

	internal void RefreshMoveUI(BasePlayer player)
	{
		if (player?.net?.connection == null) return;
		DestroyMoveUI(player);
		RecyclerSpeedUI.DestroyOverlay(player);

		var state = GetOrCreateState(player);
		if (!state.MoveModeActive) return;

		HarmonyConfig.LoadConfig();
		string parent = HarmonyConfig.Config?.OverlayParent?.Trim();
		if (string.IsNullOrEmpty(parent)) parent = "Hud";

		RecyclerSpeedUI.SendOverlayPreview(player, parent, state.UiAnchorMin, state.UiAnchorMax);
		SendMovePanel(player);
	}

	internal void DestroyMoveUI(BasePlayer player)
	{
		if (player?.net?.connection == null) return;
		var ce = CommunityEntity.ServerInstance;
		if (ce != null && !ce.IsDestroyed)
			ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), MovePanel);
	}

	internal void OnPlayerClosedRecyclerLoot(BasePlayer player)
	{
		var state = GetOrCreateState(player);
		state.MoveModeActive = false;
		state.CurrentRecycler = null;
		DestroyMoveUI(player);
		RecyclerSpeedUI.DestroyOverlay(player);
	}

	private void SendMovePanel(BasePlayer player)
	{
		var state = GetOrCreateState(player);
		var elements = new List<JObject>();

		elements.Add(new JObject
		{
			["name"] = MovePanel,
			["parent"] = "Overlay",
			["destroyUi"] = MovePanel,
			["components"] = new JArray
			{
				new JObject { ["type"] = "UnityEngine.UI.Image", ["color"] = "0.22 0.22 0.55 0.85", ["material"] = PanelMaterial, ["sprite"] = PanelSprite },
				new JObject { ["type"] = "RectTransform", ["anchormin"] = "0.70 0.12", ["anchormax"] = "0.98 0.38" },
				new JObject { ["type"] = "NeedsCursor" }
			}
		});

		AddArrowButton(elements, MovePanel + "_L", "0.05 0.35", "0.22 0.92", "cui.endtest RECYCLER_SPEED MOVE_LEFT", "←");
		AddArrowButton(elements, MovePanel + "_U", "0.25 0.35", "0.42 0.92", "cui.endtest RECYCLER_SPEED MOVE_UP", "↑");
		AddArrowButton(elements, MovePanel + "_D", "0.45 0.35", "0.62 0.92", "cui.endtest RECYCLER_SPEED MOVE_DOWN", "↓");
		AddArrowButton(elements, MovePanel + "_R", "0.65 0.35", "0.82 0.92", "cui.endtest RECYCLER_SPEED MOVE_RIGHT", "→");

		AddButton(elements, MovePanel + "_Save", MovePanel, "0.05 0.05", "0.45 0.30", "cui.endtest RECYCLER_SPEED SAVE", "Save", "0.2 0.5 0.2 0.95");
		AddCloseButton(elements, MovePanel + "_Close", MovePanel, "0.50 0.05", "0.95 0.30", "cui.endtest RECYCLER_SPEED CLOSE", "Close", "0.5 0.2 0.2 0.95");

		SendUI(player, JsonConvert.SerializeObject(elements));
	}

	private static void AddArrowButton(List<JObject> elements, string name, string amin, string amax, string cmd, string text)
	{
		elements.Add(new JObject
		{
			["name"] = name,
			["parent"] = MovePanel,
			["components"] = new JArray
			{
				new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = cmd, ["color"] = "0.2 0.4 0.6 0.95", ["material"] = PanelMaterial, ["sprite"] = PanelSprite },
				new JObject { ["type"] = "RectTransform", ["anchormin"] = amin, ["anchormax"] = amax }
			}
		});
		elements.Add(new JObject
		{
			["name"] = name + "_lbl",
			["parent"] = name,
			["components"] = new JArray
			{
				new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = text, ["fontSize"] = 18, ["color"] = "1 1 1 1", ["align"] = "MiddleCenter" },
				new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
			}
		});
	}

	private static void AddButton(List<JObject> elements, string name, string parent, string amin, string amax, string cmd, string text, string color)
	{
		elements.Add(new JObject
		{
			["name"] = name,
			["parent"] = parent,
			["components"] = new JArray
			{
				new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = cmd, ["color"] = color, ["material"] = PanelMaterial, ["sprite"] = PanelSprite },
				new JObject { ["type"] = "RectTransform", ["anchormin"] = amin, ["anchormax"] = amax }
			}
		});
		elements.Add(new JObject
		{
			["name"] = name + "_lbl",
			["parent"] = name,
			["components"] = new JArray
			{
				new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = text, ["fontSize"] = 11, ["color"] = "1 1 1 1", ["align"] = "MiddleCenter" },
				new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
			}
		});
	}

	private static void AddCloseButton(List<JObject> elements, string name, string parent, string amin, string amax, string cmd, string text, string color)
	{
		elements.Add(new JObject
		{
			["name"] = name,
			["parent"] = parent,
			["components"] = new JArray
			{
				new JObject { ["type"] = "UnityEngine.UI.Button", ["command"] = cmd, ["close"] = MovePanel, ["color"] = color, ["material"] = PanelMaterial, ["sprite"] = PanelSprite },
				new JObject { ["type"] = "RectTransform", ["anchormin"] = amin, ["anchormax"] = amax }
			}
		});
		elements.Add(new JObject
		{
			["name"] = name + "_lbl",
			["parent"] = name,
			["components"] = new JArray
			{
				new JObject { ["type"] = "UnityEngine.UI.Text", ["text"] = text, ["fontSize"] = 11, ["color"] = "1 1 1 1", ["align"] = "MiddleCenter" },
				new JObject { ["type"] = "RectTransform", ["anchormin"] = "0 0", ["anchormax"] = "1 1" }
			}
		});
	}

	private static void SendUI(BasePlayer player, string json)
	{
		if (player?.net?.connection == null || string.IsNullOrEmpty(json)) return;
		var ce = CommunityEntity.ServerInstance;
		if (ce == null || ce.IsDestroyed) return;
		try { ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json); }
		catch { }
	}

	private static void SendMessage(BasePlayer player, string msg)
	{
		if (player == null) return;
		ConsoleNetwork.SendClientCommand(player.net.connection, "chat.add", 0, 0, msg);
	}
}

public class RecyclerSpeedUIState
{
	public bool MoveModeActive;
	public string UiAnchorMin = "0.841 0.386";
	public string UiAnchorMax = "0.960 0.415";
	public Recycler CurrentRecycler;
}
