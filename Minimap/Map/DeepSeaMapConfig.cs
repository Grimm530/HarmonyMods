using System;
using Newtonsoft.Json;
using Oxide.Ext.Chaos;
using Unity.Mathematics;
using UnityEngine;

namespace Oxide.Ext.Chaos.Map;

public class DeepSeaMapConfig
{
	public class DeepSeaColors
	{
		public HexColor.Rgb Base { get; set; } = new HexColor.Rgb(new Color(0.92f, 0.88f, 0.82f));

		[JsonProperty("Gravel")]
		public HexColor.Rgb Gravel { get; set; } = new HexColor.Rgb(new Color(0.35f, 0.32f, 0.28f));

		[JsonProperty("Dirt")]
		public HexColor.Rgb Dirt { get; set; } = new HexColor.Rgb(new Color(0.65f, 0.38f, 0.25f));

		[JsonProperty("Sand")]
		public HexColor.Rgb Sand { get; set; } = new HexColor.Rgb(new Color(0.88f, 0.85f, 0.75f));

		[JsonProperty("Grass")]
		public HexColor.Rgb Grass { get; set; } = new HexColor.Rgb(new Color(0.25f, 0.52f, 0.18f));

		[JsonProperty("Forest")]
		public HexColor.Rgb Forest { get; set; } = new HexColor.Rgb(new Color(0.15f, 0.35f, 0.12f));

		[JsonProperty("Rock")]
		public HexColor.Rgb Rock { get; set; } = new HexColor.Rgb(new Color(0.35f, 0.32f, 0.3f));

		[JsonProperty("Sand Alt")]
		public HexColor.Rgb Sand2 { get; set; } = new HexColor.Rgb(new Color(0.92f, 0.88f, 0.82f));

		[JsonProperty("Pebble")]
		public HexColor.Rgb Pebble { get; set; } = new HexColor.Rgb(new Color(0.45f, 0.48f, 0.46f));

		[JsonProperty("Water")]
		public HexColor.Rgb Water { get; set; } = new HexColor.Rgb(new Color(0.12f, 0.55f, 0.65f));

		[JsonProperty("Offshore")]
		public HexColor.Rgb Offshore { get; set; } = new HexColor.Rgb(new Color(0.08f, 0.35f, 0.52f));

		public HexColor.Rgba Overlay { get; set; } = new HexColor.Rgba(new Color(0.15f, 0.15f, 0.15f, 0.15f));
	}

	[JsonIgnore]
	private float3[] _splatColors;

	[JsonProperty("Render deep sea map markers")]
	public bool RenderDeepSeaMarkers { get; set; } = true;

	[JsonProperty("Render fog of war overlay (if convar 'server.deepseafogofwar' enabled)")]
	public bool RenderFogOfWar { get; set; } = true;

	[JsonProperty("Deep Sea Colors")]
	public DeepSeaColors DeepSea { get; set; } = new DeepSeaColors();

	[JsonIgnore]
	public float3[] SplatColors
	{
		get
		{
			if (_splatColors == null)
			{
				_splatColors = new float3[Enum.GetValues(typeof(DeepSeaSplatColor)).Length];
				_splatColors[0] = DeepSea.Base;
				_splatColors[1] = DeepSea.Gravel;
				_splatColors[2] = DeepSea.Dirt;
				_splatColors[3] = DeepSea.Sand;
				_splatColors[4] = DeepSea.Grass;
				_splatColors[5] = DeepSea.Forest;
				_splatColors[6] = DeepSea.Rock;
				_splatColors[7] = DeepSea.Sand2;
				_splatColors[8] = DeepSea.Pebble;
				_splatColors[9] = DeepSea.Water;
				_splatColors[10] = DeepSea.Offshore;
			}
			return _splatColors;
		}
	}
}
