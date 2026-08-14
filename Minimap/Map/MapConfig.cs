using System;
using Newtonsoft.Json;
using Oxide.Ext.Chaos;
using Unity.Mathematics;
using UnityEngine;

namespace Oxide.Ext.Chaos.Map;

public class MapConfig
{
	public class OverworldColors
	{
		public HexColor.Rgb Base { get; set; } = new HexColor.Rgb(new Color(0.28627452f, 23f / 85f, 0.24705884f));

		public HexColor.Rgb Water { get; set; } = new HexColor.Rgb(new Color(0.16941601f, 0.31755757f, 0.36200002f));

		public HexColor.Rgb Gravel { get; set; } = new HexColor.Rgb(new Color(0.25f, 37f / 152f, 0.22039475f));

		public HexColor.Rgb Dirt { get; set; } = new HexColor.Rgb(new Color(0.6f, 0.47959462f, 0.33f));

		public HexColor.Rgb Sand { get; set; } = new HexColor.Rgb(new Color(0.7f, 0.65968585f, 0.5277487f));

		public HexColor.Rgb Grass { get; set; } = new HexColor.Rgb(new Color(0.35486364f, 0.37f, 0.2035f));

		public HexColor.Rgb Forest { get; set; } = new HexColor.Rgb(new Color(0.24843751f, 0.3f, 9f / 128f));

		public HexColor.Rgb Rock { get; set; } = new HexColor.Rgb(new Color(0.4f, 0.39379844f, 0.37519377f));

		public HexColor.Rgb Snow { get; set; } = new HexColor.Rgb(new Color(0.86274517f, 0.9294118f, 0.94117653f));

		public HexColor.Rgb Pebble { get; set; } = new HexColor.Rgb(new Color(7f / 51f, 0.2784314f, 0.2761563f));

		public HexColor.Rgb Offshore { get; set; } = new HexColor.Rgb(new Color(0.04090196f, 0.22060032f, 14f / 51f));

		public HexColor.Rgba Overlay { get; set; } = new HexColor.Rgba(new Color(0.15f, 0.15f, 0.15f, 0.25f));

		[JsonProperty("Marker Background")]
		public HexColor.Rgba MarkerBackground { get; set; } = new HexColor.Rgba(new Color(0.88f, 0.88f, 0.88f, 1f));

		[JsonProperty("Marker Foreground")]
		public HexColor.Rgba MarkerForeground { get; set; } = new HexColor.Rgba(new Color(0.1329755f, 0.1329755f, 0.1329755f, 1f));

		[JsonProperty("Monument Name")]
		public HexColor.Rgba Text { get; set; } = new HexColor.Rgba(new Color(0.1329755f, 0.1329755f, 0.1329755f, 1f));
	}

	public class UnderworldColors
	{
		public HexColor.Rgba Overlay { get; set; } = new HexColor.Rgba(new Color(0.07f, 0.082f, 0.27f, 0.8f));

		[JsonProperty("Marker Background")]
		public HexColor.Rgba MarkerBackground { get; set; } = new HexColor.Rgba(new Color(0.1329755f, 0.1329755f, 0.1329755f, 1f));

		[JsonProperty("Marker Foreground")]
		public HexColor.Rgba MarkerForeground { get; set; } = new HexColor.Rgba(new Color(0.58f, 0.58f, 0.58f, 1f));

		[JsonProperty("Monument Name")]
		public HexColor.Rgba Text { get; set; } = new HexColor.Rgba(new Color(0.88f, 0.88f, 0.88f, 1f));
	}

	[JsonIgnore]
	private int? _resolution;

	[JsonIgnore]
	private float3[] _splatColors;

	[JsonProperty("Render resolution (1024, 2048, 4096)")]
	public int Resolution { get; set; } = 2048;

	[JsonProperty("Ocean margin (the amount of ocean to render around the map)")]
	public int OceanMargin { get; set; } = 500;

	[JsonProperty("Render tunnel entrance map markers")]
	public bool RenderTunnelEntrances { get; set; } = true;

	[JsonProperty("Render monument name map markers")]
	public bool RenderMonumentNames { get; set; } = true;

	[JsonProperty("Monument name font size")]
	public int MonumentNameFontSize { get; set; } = 20;

	[JsonProperty("Render fog of war overlay (if convar 'server.fogofwar' enabled)")]
	public bool RenderFogOfWar { get; set; } = true;

	[JsonProperty("Overworld Colors")]
	public OverworldColors Overworld { get; set; } = new OverworldColors();

	[JsonProperty("Underworld Colors", NullValueHandling = NullValueHandling.Ignore)]
	public UnderworldColors Underworld { get; set; }

	[JsonIgnore]
	public int RenderResolution
	{
		get
		{
			if (!_resolution.HasValue)
			{
				int num = Mathf.Clamp(Resolution, 1024, 4096);
				_resolution = num <= 1536 ? 1024 : (num <= 3072 ? 2048 : 4096);
			}
			return _resolution.Value;
		}
	}

	[JsonIgnore]
	public float3[] SplatColors
	{
		get
		{
			if (_splatColors == null)
			{
				_splatColors = new float3[Enum.GetValues(typeof(SplatColor)).Length];
				_splatColors[0] = Overworld.Base;
				_splatColors[1] = Overworld.Gravel;
				_splatColors[2] = Overworld.Dirt;
				_splatColors[3] = Overworld.Sand;
				_splatColors[4] = Overworld.Grass;
				_splatColors[5] = Overworld.Forest;
				_splatColors[6] = Overworld.Rock;
				_splatColors[7] = Overworld.Snow;
				_splatColors[8] = Overworld.Pebble;
				_splatColors[9] = Overworld.Water;
				_splatColors[10] = Overworld.Offshore;
			}
			return _splatColors;
		}
	}
}
