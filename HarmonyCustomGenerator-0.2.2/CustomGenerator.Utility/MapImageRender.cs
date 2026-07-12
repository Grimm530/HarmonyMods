using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace CustomGenerator.Utility;

public static class MapImageRender
{
	public readonly struct Array2D<T>
	{
		private readonly T[] _items;

		private readonly int _width;

		private readonly int _height;

		public ref T this[int x, int y]
		{
			get
			{
				int num = Mathf.Clamp(x, 0, _width - 1);
				int num2 = Mathf.Clamp(y, 0, _height - 1);
				return ref _items[num2 * _width + num];
			}
		}

		public Array2D(T[] items, int width, int height)
		{
			_items = items;
			_width = width;
			_height = height;
		}

		public Bitmap ToBitmap()
		{
			//IL_002c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			Bitmap bitmap = new Bitmap(_width, _height);
			for (int i = 0; i < _height; i++)
			{
				for (int j = 0; j < _width; j++)
				{
					Color unityColor = (Color)(object)this[j, i];
					bitmap.SetPixel(j, i, unityColor.ToSystemDrawingColor());
				}
			}
			return bitmap;
		}

		public bool IsEmpty()
		{
			if (_items != null)
			{
				if (_width == 0)
				{
					return _height == 0;
				}
				return false;
			}
			return true;
		}

		public Array2D<T> Clone()
		{
			return new Array2D<T>((T[])_items.Clone(), _width, _height);
		}
	}

	private class MapMonument
	{
		public string name;

		public int x;

		public int y;

		public Indication indication;

		public string imagePath = "";
	}

	private enum Indication
	{
		None,
		Regular,
		Smaller,
		Image
	}

	private static readonly string PermanentMarkerFont = "mapimages/resources/PermanentMarker.ttf";

	private static readonly string DinProFont = "mapimages/resources/dinpro.otf";

	private static readonly string DinProFontBold = "mapimages/resources/dinprobold.otf";

	private static readonly Vector4 StartColor = new Vector4(0.28627452f, 23f / 85f, 0.24705884f, 1f);

	private static readonly Vector4 WaterColor = new Vector4(0.16941601f, 0.31755757f, 0.36200002f, 1f);

	private static readonly Vector4 GravelColor = new Vector4(0.25f, 37f / 152f, 0.22039475f, 1f);

	private static readonly Vector4 DirtColor = new Vector4(0.6f, 0.47959462f, 0.33f, 1f);

	private static readonly Vector4 SandColor = new Vector4(0.7f, 0.65968585f, 0.5277487f, 1f);

	private static readonly Vector4 GrassColor = new Vector4(0.35486364f, 0.37f, 0.2035f, 1f);

	private static readonly Vector4 ForestColor = new Vector4(0.24843751f, 0.3f, 9f / 128f, 1f);

	private static readonly Vector4 RockColor = new Vector4(0.4f, 0.39379844f, 0.37519377f, 1f);

	private static readonly Vector4 SnowColor = new Vector4(0.86274517f, 0.9294118f, 0.94117653f, 1f);

	private static readonly Vector4 PebbleColor = new Vector4(7f / 51f, 0.2784314f, 0.2761563f, 1f);

	private static readonly Vector4 OffShoreColor = new Vector4(0.04090196f, 0.22060032f, 14f / 51f, 1f);

	private static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(0.95f, 2.87f, 2.37f));

	private static readonly Vector4 Half = new Vector4(0.5f, 0.5f, 0.5f, 0.5f);

	private static FieldInfo _monuments = AccessTools.TypeByName("TerrainPath").GetField("Monuments", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	public static byte[] Render(out int imageWidth, out int imageHeight, out Color background, float scale = 0.5f, bool lossy = true, bool transparent = false, int oceanMargin = 500)
	{
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0191: Unknown result type (might be due to invalid IL or missing references)
		//IL_018a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0196: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01d3: Unknown result type (might be due to invalid IL or missing references)
		//IL_0205: Unknown result type (might be due to invalid IL or missing references)
		//IL_020a: Unknown result type (might be due to invalid IL or missing references)
		Logging.Info("-/6 | Starting rendering map...");
		Stopwatch stopwatch = Stopwatch.StartNew();
		if (lossy && transparent)
		{
			throw new ArgumentException("Rendering a transparent map is not possible when using lossy compression (JPG)");
		}
		imageWidth = 0;
		imageHeight = 0;
		background = Color.op_Implicit(OffShoreColor);
		TerrainTexturing instance = TerrainTexturing.Instance;
		if ((Object)(object)instance == (Object)null)
		{
			return null;
		}
		Terrain component = ((Component)instance).GetComponent<Terrain>();
		TerrainMeta component2 = ((Component)instance).GetComponent<TerrainMeta>();
		TerrainHeightMap terrainHeightMap = ((Component)instance).GetComponent<TerrainHeightMap>();
		TerrainSplatMap terrainSplatMap = ((Component)instance).GetComponent<TerrainSplatMap>();
		TerrainTopologyMap terrainTopologyMap = ((Component)instance).GetComponent<TerrainTopologyMap>();
		if ((Object)(object)component == (Object)null || (Object)(object)component2 == (Object)null || (Object)(object)terrainHeightMap == (Object)null || (Object)(object)terrainSplatMap == (Object)null || (Object)(object)terrainTopologyMap == (Object)null)
		{
			return null;
		}
		int mapRes = (int)((float)World.Size * Mathf.Clamp(scale, 0.1f, 4f));
		float invMapRes = 1f / (float)mapRes;
		if (mapRes <= 0)
		{
			return null;
		}
		imageWidth = mapRes + oceanMargin * 2;
		imageHeight = mapRes + oceanMargin * 2;
		Color[] array = (Color[])(object)new Color[imageWidth * imageHeight];
		Array2D<Color> output = new Array2D<Color>(array, imageWidth, imageHeight);
		float maxDepth = (transparent ? Mathf.Max(Mathf.Abs(GetHeight(0f, 0f)), 5f) : 50f);
		Vector4 offShoreColor = (transparent ? Vector4.zero : OffShoreColor);
		Vector4 waterColor = (Vector4)(transparent ? new Vector4(WaterColor.x, WaterColor.y, WaterColor.z, 0.5f) : WaterColor);
		Logging.Info("1/6 | Render begin...");
		Parallel.For(0, imageHeight, delegate(int y)
		{
			//IL_0039: Unknown result type (might be due to invalid IL or missing references)
			//IL_003e: Unknown result type (might be due to invalid IL or missing references)
			//IL_004d: Unknown result type (might be due to invalid IL or missing references)
			//IL_006e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0084: Unknown result type (might be due to invalid IL or missing references)
			//IL_0086: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00aa: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ac: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ed: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f1: Unknown result type (might be due to invalid IL or missing references)
			//IL_010a: Unknown result type (might be due to invalid IL or missing references)
			//IL_010f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0111: Unknown result type (might be due to invalid IL or missing references)
			//IL_0113: Unknown result type (might be due to invalid IL or missing references)
			//IL_012d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0132: Unknown result type (might be due to invalid IL or missing references)
			//IL_0134: Unknown result type (might be due to invalid IL or missing references)
			//IL_0136: Unknown result type (might be due to invalid IL or missing references)
			//IL_0150: Unknown result type (might be due to invalid IL or missing references)
			//IL_0155: Unknown result type (might be due to invalid IL or missing references)
			//IL_0157: Unknown result type (might be due to invalid IL or missing references)
			//IL_0159: Unknown result type (might be due to invalid IL or missing references)
			//IL_0172: Unknown result type (might be due to invalid IL or missing references)
			//IL_0177: Unknown result type (might be due to invalid IL or missing references)
			//IL_0179: Unknown result type (might be due to invalid IL or missing references)
			//IL_017b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0194: Unknown result type (might be due to invalid IL or missing references)
			//IL_0199: Unknown result type (might be due to invalid IL or missing references)
			//IL_0231: Unknown result type (might be due to invalid IL or missing references)
			//IL_0241: Unknown result type (might be due to invalid IL or missing references)
			//IL_0243: Unknown result type (might be due to invalid IL or missing references)
			//IL_0248: Unknown result type (might be due to invalid IL or missing references)
			//IL_024d: Unknown result type (might be due to invalid IL or missing references)
			//IL_024f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0251: Unknown result type (might be due to invalid IL or missing references)
			//IL_0256: Unknown result type (might be due to invalid IL or missing references)
			//IL_0260: Unknown result type (might be due to invalid IL or missing references)
			//IL_0265: Unknown result type (might be due to invalid IL or missing references)
			//IL_026a: Unknown result type (might be due to invalid IL or missing references)
			//IL_026f: Unknown result type (might be due to invalid IL or missing references)
			//IL_01dc: Unknown result type (might be due to invalid IL or missing references)
			//IL_01df: Unknown result type (might be due to invalid IL or missing references)
			//IL_0201: Unknown result type (might be due to invalid IL or missing references)
			//IL_0206: Unknown result type (might be due to invalid IL or missing references)
			//IL_0208: Unknown result type (might be due to invalid IL or missing references)
			//IL_020b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0228: Unknown result type (might be due to invalid IL or missing references)
			//IL_022d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0271: Unknown result type (might be due to invalid IL or missing references)
			//IL_0278: Unknown result type (might be due to invalid IL or missing references)
			//IL_027d: Unknown result type (might be due to invalid IL or missing references)
			//IL_02be: Unknown result type (might be due to invalid IL or missing references)
			//IL_02c5: Unknown result type (might be due to invalid IL or missing references)
			//IL_02cc: Unknown result type (might be due to invalid IL or missing references)
			//IL_02d3: Unknown result type (might be due to invalid IL or missing references)
			//IL_02da: Unknown result type (might be due to invalid IL or missing references)
			//IL_02a2: Unknown result type (might be due to invalid IL or missing references)
			//IL_02a9: Unknown result type (might be due to invalid IL or missing references)
			//IL_02b0: Unknown result type (might be due to invalid IL or missing references)
			//IL_02b7: Unknown result type (might be due to invalid IL or missing references)
			//IL_02df: Unknown result type (might be due to invalid IL or missing references)
			y -= oceanMargin;
			float y2 = (float)y * invMapRes;
			int num = mapRes + oceanMargin;
			for (int i = -oceanMargin; i < num; i++)
			{
				float x2 = (float)i * invMapRes;
				Vector4 startColor = StartColor;
				float num2 = GetHeight(x2, y2);
				Vector3 val = GetNormal(x2, y2);
				float shoreDist = GetShoreDist(x2, y2);
				bool flag = (GetTopology(x2, y2) & 0x180) != 0;
				float num3 = Math.Max(Vector3.Dot(val, SunDirection), 0f);
				startColor = Vector4.Lerp(startColor, GravelColor, GetSplat(x2, y2, 128) * GravelColor.w);
				startColor = Vector4.Lerp(startColor, PebbleColor, GetSplat(x2, y2, 64) * PebbleColor.w);
				startColor = Vector4.Lerp(startColor, RockColor, GetSplat(x2, y2, 8) * RockColor.w);
				startColor = Vector4.Lerp(startColor, DirtColor, GetSplat(x2, y2, 1) * DirtColor.w);
				startColor = Vector4.Lerp(startColor, GrassColor, GetSplat(x2, y2, 16) * GrassColor.w);
				startColor = Vector4.Lerp(startColor, ForestColor, GetSplat(x2, y2, 32) * ForestColor.w);
				startColor = Vector4.Lerp(startColor, SandColor, GetSplat(x2, y2, 4) * SandColor.w);
				startColor = Vector4.Lerp(startColor, SnowColor, GetSplat(x2, y2, 2) * SnowColor.w);
				float num4 = 0f;
				if (shoreDist > 0f)
				{
					num4 = 0f - num2;
					if (num4 <= 0f || !flag)
					{
						num4 = Mathf.Max(num4, 0.1f * shoreDist);
					}
				}
				if (num4 > 0f)
				{
					startColor = Vector4.Lerp(startColor, waterColor, Mathf.Clamp(0.5f + num4 / 5f, 0f, 1f));
					startColor = Vector4.Lerp(startColor, offShoreColor, Mathf.Clamp(num4 / maxDepth, 0f, 1f));
				}
				else
				{
					startColor += (num3 - 0.5f) * 0.65f * startColor;
					startColor = (startColor - Half) * 0.94f + Half;
				}
				startColor *= 1.05f;
				output[i + oceanMargin, y + oceanMargin] = (transparent ? new Color(startColor.x, startColor.y, startColor.z, startColor.w) : new Color(startColor.x, startColor.y, startColor.z));
			}
		});
		background = output[0, 0];
		LoadIcons(ref output, imageWidth, imageHeight, mapRes, oceanMargin);
		RenderGrid(ref output, mapRes, imageWidth, oceanMargin);
		Logging.Info($"  - Render took {stopwatch.Elapsed.Seconds}s.");
		Logging.Info("6/6 | Done! Encoding...");
		stopwatch.Stop();
		return EncodeToFile(imageWidth, imageHeight, array, lossy);
		float GetHeight(float x, float y)
		{
			return terrainHeightMap.GetHeight(x, y);
		}
		Vector3 GetNormal(float x, float y)
		{
			//IL_0008: Unknown result type (might be due to invalid IL or missing references)
			return terrainHeightMap.GetNormal(x, y);
		}
		float GetSplat(float x, float y, int mask)
		{
			return terrainSplatMap.GetSplat(x, y, mask);
		}
		int GetTopology(float x, float y)
		{
			return terrainTopologyMap.GetTopology(x, y, 16f);
		}
	}

	private static float GetShoreDist(float x, float y)
	{
		return TerrainTexturing.Instance.GetMainlandCoarseVectorToShore(x, y).shoreDist;
	}

	/// <summary>Clamps pixel coords so oil rig markers appear inside the map footprint instead of off the edge.</summary>
	private static void ClampMarkerToMapFootprint(ref int x, ref int y, int mapResolution, int oceanMargin)
	{
		int min = oceanMargin;
		int max = mapResolution + oceanMargin;
		x = Mathf.Clamp(x, min, max);
		y = Mathf.Clamp(y, min, max);
	}

	/// <summary>Projects fishing village markers from water (e.g. underwater lab) toward shore so they appear on land.</summary>
	private static Vector3 SnapFishingVillageToShore(Vector3 position, float mapSize)
	{
		if (TerrainMeta.HeightMap == null) return position;
		Vector3 samplePos = new Vector3(position.x, 0f, position.z);
		float terrainY = TerrainMeta.HeightMap.GetHeight(samplePos);
		float waterY = TerrainMeta.WaterMap != null ? TerrainMeta.WaterMap.GetHeight(samplePos) : 0f;
		if (terrainY > waterY - 0.1f) return position;
		float step = 40f;
		int maxSteps = 25;
		Vector3 toCenter = new Vector3(-position.x, 0f, -position.z);
		if (toCenter.sqrMagnitude < 1f) return position;
		toCenter.Normalize();
		for (int i = 0; i < maxSteps; i++)
		{
			position += toCenter * step;
			samplePos.Set(position.x, 0f, position.z);
			if (Mathf.Abs(position.x) > mapSize * 0.6f || Mathf.Abs(position.z) > mapSize * 0.6f) break;
			terrainY = TerrainMeta.HeightMap.GetHeight(samplePos);
			waterY = TerrainMeta.WaterMap != null ? TerrainMeta.WaterMap.GetHeight(samplePos) : 0f;
			if (terrainY > waterY - 0.1f) return position;
		}
		return position;
	}

	private static void LoadIcons(ref Array2D<Color> output, int imageWidth, int imageHeight, int mapResolution, int oceanMargin)
	{
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_005b: Unknown result type (might be due to invalid IL or missing references)
		List<MonumentInfo> obj = (List<MonumentInfo>)_monuments.GetValue(ExtConfig.tempData.terrainPath);
		Logging.Info("3/4 | Proceeding map data...");
		int num = mapResolution + oceanMargin;
		int num2 = imageWidth - num;
		float mapSize = (float)ExtConfig.tempData.mapsize;
		List<MapMonument> list = new List<MapMonument>();
		foreach (MonumentInfo item in obj)
		{
			string monumentName = GetMonumentName(item);
			Vector3 position = ((Component)item).transform.position;
			string prefabLower = ((Object)item).name?.ToLowerInvariant() ?? "";
			bool isFishingVillage = monumentName.ToLowerInvariant().Contains("fishing village");
			if (isFishingVillage && (prefabLower.Contains("underwater_lab") || prefabLower.Contains("fishing_village")))
			{
				position = SnapFishingVillageToShore(position, mapSize);
			}
			int x = (int)(((double)position.x + (double)ExtConfig.tempData.mapsize / 2.0) / (double)ExtConfig.tempData.mapsize * (double)mapResolution) + num2;
			int y = (int)(((double)position.z + (double)ExtConfig.tempData.mapsize / 2.0) / (double)ExtConfig.tempData.mapsize * (double)mapResolution) + num2;
			if (monumentName.ToLowerInvariant().Contains("oil rig") || monumentName.ToLowerInvariant().Contains("oilrig") || prefabLower.Contains("oil_rig"))
			{
				ClampMarkerToMapFootprint(ref x, ref y, mapResolution, oceanMargin);
			}
			if (monumentName.ToLower().Contains("train"))
			{
				list.Add(new MapMonument
				{
					name = monumentName,
					x = x,
					y = y,
					indication = Indication.Image
				});
			}
			else if (item.shouldDisplayOnMap && (Object)(object)item.mapIcon == (Object)null)
			{
				list.Add(new MapMonument
				{
					name = monumentName,
					x = x,
					y = y,
					indication = Indication.Regular
				});
			}
			else
			{
				list.Add(new MapMonument
				{
					name = monumentName,
					x = x,
					y = y,
					indication = Indication.None
				});
			}
		}
		RenderMonument(list, PermanentMarkerFont, ref output);
		RenderGithub(DinProFontBold, ref output, mapResolution, imageWidth);
	}

	private static void RenderText(string text, string fontPath, int fontSize, Color color, ref Array2D<Color> output, int xx, int zz)
	{
		//IL_016f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0174: Unknown result type (might be due to invalid IL or missing references)
		Bitmap bitmap = output.ToBitmap();
		PrivateFontCollection privateFontCollection = new PrivateFontCollection();
		privateFontCollection.AddFontFile(fontPath);
		Font font = new Font(privateFontCollection.Families[0], fontSize);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
			using SolidBrush brush = new SolidBrush(color);
			SizeF sizeF = graphics.MeasureString(text, font);
			float dx = (float)xx - sizeF.Width / 2f;
			float dy = (float)zz - sizeF.Height / 2f;
			graphics.TranslateTransform(dx, dy);
			graphics.RotateTransform(180f);
			graphics.ScaleTransform(-1f, 1f);
			graphics.DrawString(text, font, brush, 0f, 0f - sizeF.Height);
		}
		int width = bitmap.Width;
		int height = bitmap.Height;
		for (int i = 0; i < height; i++)
		{
			for (int j = 0; j < width; j++)
			{
				Color pixel = bitmap.GetPixel(j, i);
				output[j, i] = new Color(Mathf.Clamp((float)(int)pixel.R / 255f, 0f, 1f), Mathf.Clamp((float)(int)pixel.G / 255f, 0f, 1f), Mathf.Clamp((float)(int)pixel.B / 255f, 0f, 1f), Mathf.Clamp((float)(int)pixel.A / 255f, 0f, 1f));
			}
		}
	}

	private static void RenderGithub(string fontPath, ref Array2D<Color> output, int mapResolution, int imageResolution)
	{
		Color whiteSmoke = Color.WhiteSmoke;
		string text = "github.com/publicrust/HarmonyCustomGenerator - DeepSea Update [by aristocratos]";
		float num = 0.04f;
		int num2 = Mathf.Clamp((int)((float)imageResolution * num), 10, 30);
		using Font font = new Font(fontPath, num2);
		using Bitmap image = new Bitmap(1, 1);
		using Graphics graphics = Graphics.FromImage(image);
		int num3 = (int)graphics.MeasureString(text, font).Height;
		int xx = imageResolution / 2;
		int zz = imageResolution - num3;
		RenderText(text, fontPath, num2, whiteSmoke, ref output, xx, zz);
	}

	private static void RenderMonument(List<MapMonument> monuments, string fontPath, ref Array2D<Color> output)
	{
		Logging.Info("3/4 | Rendering monuments...");
		Color black = Color.Black;
		foreach (MapMonument monument in monuments)
		{
			if (monument.indication != 0 && monument.indication != Indication.Image)
			{
				int x = monument.x;
				int y = monument.y;
				string name = monument.name;
				int fontSize = ((monument.indication == Indication.Regular) ? 20 : 11);
				RenderText(name, fontPath, fontSize, black, ref output, x, y);
			}
		}
	}

	private static void RenderGrid(ref Array2D<Color> output, int mapResolution, int imageWidth, int oceanMargin)
	{
		//IL_033c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0341: Unknown result type (might be due to invalid IL or missing references)
		Logging.Info("4/6 | Rendering grid...");
		Color color = Color.FromArgb(120, 0, 0, 0);
		Bitmap bitmap = output.ToBitmap();
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			float num = 146.3f;
			float num2 = (float)mapResolution / ((float)ExtConfig.tempData.mapsize / num);
			using Pen pen = new Pen(color, 1f);
			int num3 = (int)((float)ExtConfig.tempData.mapsize / num);
			for (int i = 0; i <= num3; i++)
			{
				float num4 = (float)oceanMargin + (float)i * num2;
				if (num4 >= (float)oceanMargin && num4 <= (float)(imageWidth - oceanMargin))
				{
					graphics.DrawLine(pen, num4, oceanMargin, num4, imageWidth - oceanMargin);
				}
			}
			for (int j = 0; j <= num3; j++)
			{
				float num5 = (float)oceanMargin + (float)j * num2;
				if (num5 >= (float)oceanMargin && num5 <= (float)(imageWidth - oceanMargin))
				{
					graphics.DrawLine(pen, oceanMargin, num5, imageWidth - oceanMargin, num5);
				}
			}
			Font font = new Font("Arial", 12f, FontStyle.Bold);
			float num6 = 5f;
			using (SolidBrush brush = new SolidBrush(color))
			{
				for (int k = 0; k < num3; k++)
				{
					for (int l = 0; l < num3; l++)
					{
						float num7 = (float)oceanMargin + (float)k * num2;
						float num8 = (float)oceanMargin + (float)l * num2;
						float num9 = num7 + num2;
						float num10 = num8 + num2;
						bool num11 = num7 >= (float)oceanMargin && num9 <= (float)(imageWidth - oceanMargin) && num8 >= (float)oceanMargin && num10 <= (float)(imageWidth - oceanMargin);
						bool flag = num7 >= (float)oceanMargin && num7 <= (float)(imageWidth - oceanMargin) && num8 >= (float)oceanMargin && num8 <= (float)(imageWidth - oceanMargin) && num9 > (float)(imageWidth - oceanMargin);
						if (num11 || flag)
						{
							string s = ((k <= 25) ? $"{(char)(65 + k)}{num3 - l}" : $"{(char)(65 + (k / 26 - 1))}{(char)(65 + k % 26)}{num3 - l}");
							float dx = num7 + num6;
							float dy = num8 + num6 + (num2 - num6 * 6f);
							graphics.TranslateTransform(dx, dy);
							graphics.RotateTransform(180f);
							graphics.ScaleTransform(-1f, 1f);
							graphics.DrawString(s, font, brush, 0f, -font.Height);
							graphics.ResetTransform();
						}
					}
				}
			}
			font.Dispose();
		}
		int width = bitmap.Width;
		int height = bitmap.Height;
		for (int m = 0; m < height; m++)
		{
			for (int n = 0; n < width; n++)
			{
				Color pixel = bitmap.GetPixel(n, m);
				output[n, m] = new Color(Mathf.Clamp((float)(int)pixel.R / 255f, 0f, 1f), Mathf.Clamp((float)(int)pixel.G / 255f, 0f, 1f), Mathf.Clamp((float)(int)pixel.B / 255f, 0f, 1f), Mathf.Clamp((float)(int)pixel.A / 255f, 0f, 1f));
			}
		}
	}

	private static byte[] EncodeToFile(int width, int height, Color[] pixels, bool lossy)
	{
		//IL_000c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0012: Expected O, but got Unknown
		Stopwatch stopwatch = Stopwatch.StartNew();
		Texture2D val = null;
		try
		{
			val = new Texture2D(width, height, (TextureFormat)4, false);
			val.SetPixels(pixels);
			val.Apply();
			return lossy ? ImageConversion.EncodeToJPG(val, 85) : ImageConversion.EncodeToPNG(val);
		}
		finally
		{
			if ((Object)(object)val != (Object)null)
			{
				Object.Destroy((Object)(object)val);
			}
			stopwatch.Stop();
			Logging.Info($"  - Encoding took {stopwatch.Elapsed.Seconds}s.");
		}
	}

	public static string GetMonumentName(MonumentInfo monument)
	{
		object obj;
		if (monument == null)
		{
			obj = null;
		}
		else
		{
			Phrase displayPhrase = monument.displayPhrase;
			obj = ((displayPhrase == null) ? null : displayPhrase.english?.Replace("\n", ""));
		}
		string text = (string)obj;
		if (string.IsNullOrEmpty(text))
		{
			text = ((monument.Type == MonumentType.Cave) ? "Cave" : ((!((Object)monument).name.Contains("power_sub")) ? ((Object)monument).name : "Power Sub Station"));
		}
		return text;
	}
}
