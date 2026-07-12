using System;
using System.Collections.Generic;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace TCUpgrade;

public static class CUIHelper
{
	private const string PanelMaterial = "assets/content/ui/namefontmaterial.mat";

	private const string PanelSprite = "assets/content/ui/ui.background.tile.psd";

	private const string LegacyCommandPrefix = "cui.endtest SENDCMD";

	private static CommunityEntity GetCommunityEntity()
	{
		CommunityEntity serverInstance = CommunityEntity.ServerInstance;
		if ((Object)(object)serverInstance != (Object)null && !serverInstance.IsDestroyed)
		{
			return serverInstance;
		}
		if (BaseNetworkable.serverEntities != null)
		{
			foreach (var entity in BaseNetworkable.serverEntities)
			{
				if (entity is CommunityEntity communityEntity && (Object)(object)communityEntity != (Object)null && !communityEntity.IsDestroyed)
				{
					return communityEntity;
				}
			}
		}
		return null;
	}

	public static void DestroyUi(BasePlayer player, string name)
	{
		object obj;
		if (player == null)
		{
			obj = null;
		}
		else
		{
			Networkable net = player.net;
			obj = ((net != null) ? net.connection : null);
		}
		if (obj != null)
		{
			CommunityEntity communityEntity = GetCommunityEntity();
			if ((Object)(object)communityEntity != (Object)null)
			{
				communityEntity.ClientRPC(RpcTarget.Player("DestroyUI", player.net.connection), name);
			}
		}
	}

	public static void AddUi(BasePlayer player, string json)
	{
		object obj;
		if (player == null)
		{
			obj = null;
		}
		else
		{
			Networkable net = player.net;
			obj = ((net != null) ? net.connection : null);
		}
		if (obj == null || string.IsNullOrEmpty(json))
		{
			return;
		}
		CommunityEntity communityEntity = GetCommunityEntity();
		if ((Object)(object)communityEntity != (Object)null)
		{
			communityEntity.ClientRPC(RpcTarget.Player("AddUI", player.net.connection), json);
			return;
		}
		TCUpgradeConfig.ConfigData config = TCUpgradeConfig.Config;
		if (config != null && config.Debug)
		{
			Debug.LogWarning((object)"[TCUpgrade] AddUi: CommunityEntity not found - UI will not display");
		}
	}

	public static void AddUi(BasePlayer player, List<JObject> elements)
	{
		AddUi(player, JsonConvert.SerializeObject((object)elements));
	}

	public static JObject Container(string name, string parent, string anchorMin, string anchorMax, string offsetMin, string offsetMax, bool needsCursor = false)
	{
		JArray val = new JArray();
		val.Add((JToken)new JObject
		{
			["type"] = "RectTransform",
			["anchormin"] = anchorMin,
			["anchormax"] = anchorMax,
			["offsetmin"] = offsetMin,
			["offsetmax"] = offsetMax
		});
		JArray val2 = val;
		if (needsCursor)
		{
			val2.Add((JToken)(object)JObject.FromObject((object)new Dictionary<string, string> { ["type"] = "NeedsCursor" }));
		}
		return new JObject
		{
			["name"] = name,
			["parent"] = parent,
			["components"] = (JToken)(object)val2
		};
	}

	public static JObject Panel(string name, string parent, string color, string anchorMin, string anchorMax, string offsetMin, string offsetMax, bool needsCursor = false)
	{
		JArray val = new JArray();
		val.Add((JToken)(object)JObject.FromObject((object)new Dictionary<string, object>
		{
			["type"] = "UnityEngine.UI.Image",
			["color"] = color,
			["material"] = "assets/content/ui/namefontmaterial.mat",
			["sprite"] = "assets/content/ui/ui.background.tile.psd"
		}));
		val.Add((JToken)(object)JObject.FromObject((object)new Dictionary<string, object>
		{
			["type"] = "RectTransform",
			["anchormin"] = anchorMin,
			["anchormax"] = anchorMax,
			["offsetmin"] = offsetMin,
			["offsetmax"] = offsetMax
		}));
		JArray val2 = val;
		if (needsCursor)
		{
			val2.Add((JToken)(object)JObject.FromObject((object)new Dictionary<string, string> { ["type"] = "NeedsCursor" }));
		}
		return new JObject
		{
			["name"] = name,
			["parent"] = parent,
			["components"] = (JToken)(object)val2
		};
	}

	public static JObject Label(string name, string parent, string text, int fontSize, string anchorMin, string anchorMax, string color = "1 1 1 0.6", string align = "MiddleCenter")
	{
		JObject val = new JObject
		{
			["name"] = name,
			["parent"] = parent
		};
		JArray val2 = new JArray();
		val2.Add((JToken)new JObject
		{
			["type"] = "UnityEngine.UI.Text",
			["text"] = text,
			["fontSize"] = fontSize,
			["color"] = color,
			["align"] = align
		});
		val2.Add((JToken)new JObject
		{
			["type"] = "RectTransform",
			["anchormin"] = anchorMin,
			["anchormax"] = anchorMax
		});
		val["components"] = (JToken)val2;
		return val;
	}

	public static List<JObject> Button(string name, string parent, string color, string text, int fontSize, string anchorMin, string anchorMax, string command, int iconItemId = 0, bool iconOnLeft = false)
	{
		command = NormalizeButtonCommand(command);
		List<JObject> list = new List<JObject>();
		JObject val = new JObject
		{
			["name"] = name,
			["parent"] = parent
		};
		JArray val2 = new JArray();
		val2.Add((JToken)new JObject
		{
			["type"] = "UnityEngine.UI.Button",
			["command"] = command,
			["color"] = color,
			["material"] = "assets/content/ui/namefontmaterial.mat",
			["sprite"] = "assets/content/ui/ui.background.tile.psd"
		});
		val2.Add((JToken)new JObject
		{
			["type"] = "RectTransform",
			["anchormin"] = anchorMin,
			["anchormax"] = anchorMax
		});
		val["components"] = (JToken)val2;
		list.Add(val);
		List<JObject> list2 = list;
		if (!string.IsNullOrEmpty(text))
		{
			string text2 = "0 0";
			string text3 = "1 1";
			if (iconItemId != 0)
			{
				if (iconOnLeft)
				{
					text2 = "0.28 0";
					text3 = "1 1";
				}
				else
				{
					text3 = "0.65 1";
				}
			}
			JObject val3 = new JObject
			{
				["name"] = name + "_lbl",
				["parent"] = name
			};
			JArray val4 = new JArray();
			val4.Add((JToken)new JObject
			{
				["type"] = "UnityEngine.UI.Text",
				["text"] = text,
				["fontSize"] = fontSize,
				["align"] = "MiddleCenter"
			});
			val4.Add((JToken)new JObject
			{
				["type"] = "RectTransform",
				["anchormin"] = text2,
				["anchormax"] = text3
			});
			val3["components"] = (JToken)val4;
			list2.Add(val3);
		}
		if (iconItemId != 0)
		{
			string anchorMin2;
			string anchorMax2;
			if (!iconOnLeft)
			{
				anchorMin2 = "0.72 0.15";
				anchorMax2 = "0.95 0.85";
			}
			else
			{
				anchorMin2 = "0.02 0.15";
				anchorMax2 = "0.25 0.85";
			}
			JObject val5 = Image(name + "_icon", name, iconItemId, 0uL, anchorMin2, anchorMax2);
			if (val5 != null)
			{
				list2.Add(val5);
			}
		}
		return list2;
	}

	public static List<JObject> ButtonWithStatusIndicator(string name, string parent, string greyBoxColor, string indicatorColor, string text, int fontSize, string boxAnchorMin, string boxAnchorMax, string labelAnchorMin, string labelAnchorMax, string command)
	{
		command = NormalizeButtonCommand(command);
		List<JObject> list = new List<JObject>();
		JObject val = new JObject
		{
			["name"] = name,
			["parent"] = parent
		};
		JArray val2 = new JArray();
		val2.Add((JToken)new JObject
		{
			["type"] = "UnityEngine.UI.Button",
			["command"] = command,
			["color"] = greyBoxColor,
			["material"] = "assets/content/ui/namefontmaterial.mat",
			["sprite"] = "assets/content/ui/ui.background.tile.psd"
		});
		val2.Add((JToken)new JObject
		{
			["type"] = "RectTransform",
			["anchormin"] = boxAnchorMin,
			["anchormax"] = boxAnchorMax
		});
		val["components"] = (JToken)val2;
		list.Add(val);
		JObject val3 = new JObject
		{
			["name"] = name + "_box",
			["parent"] = name
		};
		JArray val4 = new JArray();
		val4.Add((JToken)new JObject
		{
			["type"] = "UnityEngine.UI.Image",
			["color"] = indicatorColor,
			["material"] = "assets/content/ui/namefontmaterial.mat",
			["sprite"] = "assets/content/ui/ui.background.tile.psd"
		});
		val4.Add((JToken)new JObject
		{
			["type"] = "RectTransform",
			["anchormin"] = "0.1 0.1",
			["anchormax"] = "0.9 0.9"
		});
		val3["components"] = (JToken)val4;
		list.Add(val3);
		JObject val5 = new JObject
		{
			["name"] = name + "_lbl",
			["parent"] = parent
		};
		JArray val6 = new JArray();
		val6.Add((JToken)new JObject
		{
			["type"] = "UnityEngine.UI.Text",
			["text"] = text,
			["fontSize"] = fontSize,
			["align"] = "MiddleLeft"
		});
		val6.Add((JToken)new JObject
		{
			["type"] = "RectTransform",
			["anchormin"] = labelAnchorMin,
			["anchormax"] = labelAnchorMax
		});
		val5["components"] = (JToken)val6;
		list.Add(val5);
		return list;
	}

	public static JObject RawImage(string name, string parent, string pngId, string anchorMin, string anchorMax, string color = "1 1 1 1")
	{
		if (string.IsNullOrEmpty(pngId))
		{
			return null;
		}
		JObject val = new JObject
		{
			["name"] = name,
			["parent"] = parent
		};
		JArray val2 = new JArray();
		val2.Add((JToken)new JObject
		{
			["type"] = "UnityEngine.UI.RawImage",
			["png"] = pngId,
			["color"] = color
		});
		val2.Add((JToken)new JObject
		{
			["type"] = "RectTransform",
			["anchormin"] = anchorMin,
			["anchormax"] = anchorMax
		});
		val["components"] = (JToken)val2;
		return val;
	}

	public static JObject RawImageSteamId(string name, string parent, ulong steamId, string anchorMin, string anchorMax, string color = "1 1 1 1")
	{
		if (steamId == 0L)
		{
			return null;
		}
		JObject val = new JObject
		{
			["name"] = name,
			["parent"] = parent
		};
		JArray val2 = new JArray();
		val2.Add((JToken)new JObject
		{
			["type"] = "UnityEngine.UI.RawImage",
			["steamid"] = steamId.ToString(),
			["color"] = color
		});
		val2.Add((JToken)new JObject
		{
			["type"] = "RectTransform",
			["anchormin"] = anchorMin,
			["anchormax"] = anchorMax
		});
		val["components"] = (JToken)val2;
		return val;
	}

	public static JObject RawImageUrl(string name, string parent, string url, string anchorMin, string anchorMax, string color = "1 1 1 1")
	{
		if (string.IsNullOrEmpty(url) || !url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		JObject val = new JObject
		{
			["name"] = name,
			["parent"] = parent
		};
		JArray val2 = new JArray();
		val2.Add((JToken)new JObject
		{
			["type"] = "UnityEngine.UI.RawImage",
			["url"] = url,
			["color"] = color
		});
		val2.Add((JToken)new JObject
		{
			["type"] = "RectTransform",
			["anchormin"] = anchorMin,
			["anchormax"] = anchorMax
		});
		val["components"] = (JToken)val2;
		return val;
	}

	public static JObject ScrollView(string name, string parent, string anchorMin, string anchorMax, string offsetMin, string offsetMax, int contentHeight, int contentWidth = 0)
	{
		bool flag = contentWidth > 0;
		JObject val = ((!flag) ? new JObject
		{
			["anchormin"] = "0 1",
			["anchormax"] = "1 1",
			["offsetmin"] = "0 " + -contentHeight,
			["offsetmax"] = "0 0"
		} : new JObject
		{
			["anchormin"] = "0.5 1",
			["anchormax"] = "0.5 1",
			["pivot"] = "0.5 1",
			["offsetmin"] = -contentWidth / 2 + " " + -contentHeight,
			["offsetmax"] = contentWidth / 2 + " 0"
		});
		JObject val2 = new JObject
		{
			["name"] = name,
			["parent"] = parent
		};
		JArray val3 = new JArray();
		val3.Add((JToken)new JObject
		{
			["type"] = "UnityEngine.UI.Image",
			["color"] = "0 0 0 0"
		});
		val3.Add((JToken)new JObject
		{
			["type"] = "UnityEngine.UI.ScrollView",
			["horizontal"] = flag,
			["vertical"] = true,
			["contentTransform"] = (JToken)(object)val,
			["movementType"] = "Elastic",
			["elasticity"] = 0.25f,
			["inertia"] = true,
			["decelerationRate"] = 0.3f,
			["scrollSensitivity"] = 24f
		});
		val3.Add((JToken)new JObject
		{
			["type"] = "RectTransform",
			["anchormin"] = anchorMin,
			["anchormax"] = anchorMax,
			["offsetmin"] = offsetMin,
			["offsetmax"] = offsetMax
		});
		val2["components"] = (JToken)val3;
		return val2;
	}

	public static string GetImageUrl(string imgKey)
	{
		string text = TCUpgradeConfig.Config?.ImageUrlBase?.Trim();
		if (string.IsNullOrEmpty(text))
		{
			return null;
		}
		return (text.EndsWith("/") ? text : (text + "/")) + imgKey + ".png";
	}

	public static JObject Image(string name, string parent, int itemId, ulong skinId, string anchorMin, string anchorMax)
	{
		if (itemId == 0)
		{
			return null;
		}
		JObject val = new JObject
		{
			["type"] = "UnityEngine.UI.Image",
			["itemid"] = itemId
		};
		if (skinId != 0L)
		{
			val["skinid"] = (long)skinId;
		}
		JObject val2 = new JObject
		{
			["name"] = name,
			["parent"] = parent
		};
		JArray val3 = new JArray();
		val3.Add((JToken)(object)val);
		val3.Add((JToken)new JObject
		{
			["type"] = "RectTransform",
			["anchormin"] = anchorMin,
			["anchormax"] = anchorMax
		});
		val2["components"] = (JToken)val3;
		return val2;
	}

	public static List<JObject> ButtonWithImage(string name, string parent, string color, string command, string anchorMin, string anchorMax, int? itemId = null, ulong? skinId = null)
	{
		command = NormalizeButtonCommand(command);
		List<JObject> list = new List<JObject>();
		JObject val = new JObject
		{
			["name"] = name,
			["parent"] = parent
		};
		JArray val2 = new JArray();
		val2.Add((JToken)new JObject
		{
			["type"] = "UnityEngine.UI.Button",
			["command"] = command,
			["color"] = color,
			["material"] = "assets/content/ui/namefontmaterial.mat",
			["sprite"] = "assets/content/ui/ui.background.tile.psd"
		});
		val2.Add((JToken)new JObject
		{
			["type"] = "RectTransform",
			["anchormin"] = anchorMin,
			["anchormax"] = anchorMax
		});
		val["components"] = (JToken)val2;
		list.Add(val);
		List<JObject> list2 = list;
		if (itemId.HasValue)
		{
			JObject val3 = new JObject
			{
				["type"] = "UnityEngine.UI.Image",
				["itemid"] = itemId.Value
			};
			if (skinId.HasValue && skinId.Value != 0L)
			{
				val3["skinid"] = (long)skinId.Value;
			}
			JObject val4 = new JObject { ["parent"] = name };
			JArray val5 = new JArray();
			val5.Add((JToken)(object)val3);
			val5.Add((JToken)new JObject
			{
				["type"] = "RectTransform",
				["anchormin"] = "0.1 0.1",
				["anchormax"] = "0.9 0.9"
			});
			val4["components"] = (JToken)val5;
			list2.Add(val4);
		}
		return list2;
	}

	private static string NormalizeButtonCommand(string command)
	{
		if (string.IsNullOrWhiteSpace(command))
			return command;
		if (!command.StartsWith(LegacyCommandPrefix, StringComparison.Ordinal))
			return command;

		string args = command.Substring(LegacyCommandPrefix.Length).TrimStart();
		return string.IsNullOrEmpty(args) ? "SENDCMD" : "SENDCMD " + args;
	}
}
