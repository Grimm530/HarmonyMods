using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Network;
using UnityEngine;

namespace RecyclerSpeed;

/// <summary>
/// CUI overlay: grey panel + green text over the static "60% EFFICIENCY, 5 SEC".
/// Same approach as Radar - direct ClientRPC, no reflection. Deferred 2 frames so loot panel renders first.
/// </summary>
internal static class RecyclerSpeedUI
{
	public const string PanelName = "RecyclerSpeed_Overlay";

	private const string PanelMaterial = "assets/content/ui/namefontmaterial.mat";
	private const string PanelSprite = "assets/content/ui/ui.background.tile.psd";

	public static void SendOverlay(BasePlayer player, Recycler recycler)
	{
		HarmonyConfig.LoadConfig();
		if (HarmonyConfig.Config == null || !HarmonyConfig.Config.ShowOverlay)
			return;

		var mod = RecyclerSpeedMod.Instance;
		if (mod != null)
			mod.GetOrCreateState(player).CurrentRecycler = recycler;

		string text = FormatRecyclerStatsText(recycler);

		GetAnchors(player, out string parent, out string anchormin, out string anchormax);
		bool addClickToggle = (player?.IsAdmin ?? false) || (player?.IsDeveloper ?? false);
		ServerMgr.Instance?.StartCoroutine(DeferredSendOverlay(player, text, parent, anchormin, anchormax, addClickToggle));
	}

	/// <summary>
	/// Preview overlay for /recyclerUI move mode. Same style, placeholder text.
	/// </summary>
	public static void SendOverlayPreview(BasePlayer player, string parent, string anchormin, string anchormax)
	{
		SendOverlayInternal(player, "60% EFFICIENCY, 2.5 SEC", parent, anchormin, anchormax, false);
	}

	/// <summary>
	/// Resend overlay with current state anchors and text from the stored recycler (when arrow clicked to move).
	/// Caller must DestroyOverlay first. Uses destroyUi so AddUI replaces any remnant.
	/// </summary>
	public static void RefreshOverlayFromRecycler(BasePlayer player)
	{
		var mod = RecyclerSpeedMod.Instance;
		var state = mod?.GetOrCreateState(player);
		var recycler = state?.CurrentRecycler;
		if (recycler == null || recycler.IsDestroyed)
			return;

		string text = FormatRecyclerStatsText(recycler);

		HarmonyConfig.LoadConfig();
		string parent = HarmonyConfig.Config?.OverlayParent?.Trim();
		if (string.IsNullOrEmpty(parent)) parent = "Hud";

		SendOverlayInternal(player, text, parent, state.UiAnchorMin, state.UiAnchorMax, true);
	}

	/// <summary>
	/// Uses GetRecyclerStats (replaces removed GetRecycleThinkDuration / hard-coded efficiencies).
	/// Duration already reflects RecyclerSpeedMultiplier via the GetRecyclerStats postfix.
	/// </summary>
	private static string FormatRecyclerStatsText(Recycler recycler)
	{
		recycler.GetRecyclerStats(out float efficiency, out float duration);
		int percent = Mathf.RoundToInt(efficiency * 100f);
		return $"{percent}% EFFICIENCY, {duration:0.#} SEC";
	}

	private static void GetAnchors(BasePlayer player, out string parent, out string anchormin, out string anchormax)
	{
		HarmonyConfig.LoadConfig();
		parent = HarmonyConfig.Config?.OverlayParent?.Trim();
		if (string.IsNullOrEmpty(parent)) parent = "Hud";
		anchormin = HarmonyConfig.Config?.OverlayAnchormin ?? "0.841 0.386";
		anchormax = HarmonyConfig.Config?.OverlayAnchormax ?? "0.960 0.415";
	}

	private static void SendOverlayInternal(BasePlayer player, string text, string parent, string anchormin, string anchormax, bool addClickToggle = false)
	{
		var elements = new List<object>
		{
			new Dictionary<string, object>
			{
				["name"] = PanelName,
				["parent"] = parent,
				["destroyUi"] = PanelName,
				["components"] = new List<object>
				{
					new Dictionary<string, object>
					{
						["type"] = "UnityEngine.UI.Image",
						["color"] = "0.32 0.35 0.28 0.98",
						["material"] = PanelMaterial,
						["sprite"] = PanelSprite
					},
					new Dictionary<string, object>
					{
						["type"] = "RectTransform",
						["anchormin"] = anchormin,
						["anchormax"] = anchormax
					}
				}
			},
			new Dictionary<string, object>
			{
				["name"] = PanelName + "_Text",
				["parent"] = PanelName,
				["components"] = new List<object>
				{
					new Dictionary<string, object>
					{
						["type"] = "UnityEngine.UI.Text",
						["text"] = text,
						["fontSize"] = 12,
						["align"] = "MiddleCenter",
						["color"] = "0.2 0.9 0.3 1.0"
					},
					new Dictionary<string, object>
					{
						["type"] = "RectTransform",
						["anchormin"] = "0 0",
						["anchormax"] = "1 1",
						["offsetmin"] = "4 2",
						["offsetmax"] = "-4 -2"
					}
				}
			}
		};

		if (addClickToggle)
		{
			elements.Add(new Dictionary<string, object>
			{
				["name"] = PanelName + "_Click",
				["parent"] = PanelName,
				["components"] = new List<object>
				{
					new Dictionary<string, object>
					{
						["type"] = "UnityEngine.UI.Button",
						["command"] = "cui.endtest RECYCLER_SPEED TOGGLE_MOVE",
						["color"] = "0.18 0.20 0.16 0.02",
						["material"] = PanelMaterial,
						["sprite"] = PanelSprite
					},
					new Dictionary<string, object>
					{
						["type"] = "RectTransform",
						["anchormin"] = "0 0",
						["anchormax"] = "1 1"
					}
				}
			});
		}

		string json = JsonConvert.SerializeObject(elements);
		SendUI(player, json);
	}

	private static IEnumerator DeferredSendOverlay(BasePlayer player, string text, string parent, string anchormin, string anchormax, bool addClickToggle)
	{
		yield return null;
		yield return null;
		if (player == null || !player.IsConnected) yield break;

		SendOverlayInternal(player, text, parent, anchormin, anchormax, addClickToggle);
	}

	public static void DestroyOverlay(BasePlayer player)
	{
		if (player?.net?.connection == null) return;
		var ce = CommunityEntity.ServerInstance;
		if (ce != null && !ce.IsDestroyed)
			ce.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), PanelName);
	}

	private static void SendUI(BasePlayer player, string json)
	{
		if (player?.net?.connection == null || string.IsNullOrEmpty(json)) return;
		var ce = CommunityEntity.ServerInstance;
		if (ce == null || ce.IsDestroyed) return;
		ce.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json);
	}
}
