using System;
using System.Collections.Generic;
using ConVar;
using MinimapHarmony;
using Oxide.Ext.Chaos.UIFramework;
using Oxide.Plugins;
using Unity.Collections;
using UnityEngine;

using UIAnchor = Oxide.Ext.Chaos.UIFramework.Anchor;
using UILayer = Oxide.Ext.Chaos.UIFramework.Layer;

namespace Oxide.Ext.Chaos.Map;

public abstract class ChaosMapPlugin
{
	protected class Overlay
	{
		public string Name;
		public bool IsToggleable;
		public int Priority;
		public string ToggleIcon;
		public string TogglePng;
		public Func<BaseMapUser, bool> CanViewOverlay;
		public Action<BaseMapUser, string, string, bool> RenderOverlay;
		public Func<BaseMapUser, string, bool, UpdateComponent> UpdateOverlayState;
	}

	protected MapConfig _mapConfig;
	protected DeepSeaMapConfig _deepSeaMapConfig;

	protected string PluginIdentifier;
	public CommandCallbackHandler CallbackHandler;

	protected Oxide.Ext.Chaos.UIFramework.Color BorderColor;
	protected Oxide.Ext.Chaos.UIFramework.Color MarkerColor;
	protected Oxide.Ext.Chaos.UIFramework.Color ForegroundColor;
	protected Oxide.Ext.Chaos.UIFramework.Color TextColor;

	private static readonly List<Overlay> Overlays = new List<Overlay>();
	protected static event Action onAvailableOverlaysChanged;
	protected static event Action<Overlay> onOverlayUpdated;

	private const string MapArrow = "maparrow.{0}";
	private const int ArrowDegrees = 15;
	private static readonly Hash<int, string> ArrowImages = new Hash<int, string>();

	protected const int FogCellsTotal = 16;
	protected const int FogCellsPerRow = 4;
	protected const string FogMaterial = "assets/content/ui/gameui/map/maplayers.mat";
	protected const string FogOverlay = "{0}.fogoverlay";
	protected static Oxide.Ext.Chaos.UIFramework.Color FogColor = new Oxide.Ext.Chaos.UIFramework.Color(0.16f, 0.16f, 0.14f);

	private readonly Queue<(BaseMapUser mapUser, BasePlayer.FogMode fogMode, int x, int y, byte[] bytes)> _fogCellUpdateQueue
		= new Queue<(BaseMapUser, BasePlayer.FogMode, int, int, byte[])>();
	private bool _isProcessingFogCellQueue;

	private readonly List<Overlay> _overlayBuffer = new List<Overlay>();
	private readonly List<UpdateComponent> _updateBuffer = new List<UpdateComponent>();

	public abstract string Title { get; }

	protected static string CurrentWorldID(int renderResolution)
	{
		// Custom map URLs are too long for Windows cache filenames; hash the identity instead.
		string payload = $"{World.Size}|{World.Seed}|{World.Salt}|{World.Url}|{renderResolution}";
		return $"{World.Size}_{World.Seed}_{HashHex(payload)}_{renderResolution}";
	}

	private static string HashHex(string value)
	{
		using (var sha = System.Security.Cryptography.SHA1.Create())
		{
			byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value ?? ""));
			var sb = new System.Text.StringBuilder(16);
			for (int i = 0; i < 8; i++)
				sb.Append(bytes[i].ToString("x2"));
			return sb.ToString();
		}
	}

	protected bool HasArrowImages()
	{
		for (int deg = 0; deg < 360; deg += ArrowDegrees)
		{
			if (!TryGetImage(string.Format(MapArrow, deg), out string value))
				return false;
			ArrowImages[deg] = value;
		}
		return true;
	}

	protected void ImportMapArrows()
	{
		ImageStore.ImportEmbeddedArrows();
		for (int deg = 0; deg < 360; deg += ArrowDegrees)
		{
			if (TryGetImage(string.Format(MapArrow, deg), out string image))
				ArrowImages[deg] = image;
		}
	}

	protected static string GetClosestDirectionIcon(float rotation)
	{
		int key = MathExtensions.RoundToNearestDegrees(rotation, ArrowDegrees);
		if (ArrowImages.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
			return value;
		return ArrowImages[0];
	}

	protected bool TryGetImage(string name, out string value)
	{
		return ImageStore.TryGetImage(name, out value);
	}

	protected void StoreImageData(string name, NativeArray<UnityEngine.Color> src, Texture2D texture2D, bool transparent, Action<string> callback = null, bool writeToDisk = false)
	{
		texture2D.SetPixels(src.ToArray());
		texture2D.Apply(updateMipmaps: false);
		byte[] bytes = transparent ? texture2D.EncodeToPNG() : texture2D.EncodeToJPG();
		StoreImageData(name, bytes, transparent, callback, writeToDisk);
	}

	protected void StoreImageData(string name, NativeArray<Color32> src, Texture2D texture2D, bool transparent, Action<string> callback = null, bool writeToDisk = false)
	{
		texture2D.SetPixels32(src.ToArray());
		texture2D.Apply(updateMipmaps: false);
		byte[] bytes = transparent ? texture2D.EncodeToPNG() : texture2D.EncodeToJPG();
		StoreImageData(name, bytes, transparent, callback, writeToDisk);
	}

	protected void StoreImageData(string name, byte[] bytes, bool transparent, Action<string> callback = null, bool writeToDisk = false)
	{
		ImageStore.AddImageData(name, bytes, crc => callback?.Invoke(crc));
	}

	protected virtual void SetupInterface(MapConfig mapConfig, DeepSeaMapConfig deepSeaMapConfig)
	{
		PluginIdentifier = (Title ?? "minimap").ToLowerInvariant().Replace(" ", "");
		_mapConfig = mapConfig ?? new MapConfig();
		_deepSeaMapConfig = deepSeaMapConfig ?? new DeepSeaMapConfig { RenderFogOfWar = false };
	}

	protected virtual BaseContainer CreateToggleContainer(float width, float height, string parent)
	{
		return ImageContainer.Create($"ui.{PluginIdentifier}.toggles", UILayer.Hud, UIAnchor.BottomLeft, new Offset(0f, 0f, width, height))
			.WithColor(BorderColor)
			.WithParent(parent);
	}

	protected virtual void RenderToggle(BaseMapUser mapUser, BaseContainer parent, Offset offset, Overlay overlay, bool isActive)
	{
		ImageContainer.Create(parent, UIAnchor.BottomLeft, offset).WithColor(ForegroundColor).WithChildren(close =>
		{
			if (!string.IsNullOrEmpty(overlay.ToggleIcon))
			{
				ImageContainer.Create(close, UIAnchor.FullStretch, new Offset(2f, 2f, -2f, -2f))
					.WithSprite(overlay.ToggleIcon)
					.WithColor(isActive ? TextColor : BorderColor)
					.WithName($"ui.{PluginIdentifier}.toggle.{overlay.Name}");
			}
			else if (!string.IsNullOrEmpty(overlay.TogglePng))
			{
				RawImageContainer.Create(close, UIAnchor.FullStretch, new Offset(2f, 2f, -2f, -2f))
					.WithPNG(overlay.TogglePng)
					.WithColor(isActive ? TextColor : BorderColor)
					.WithName($"ui.{PluginIdentifier}.toggle.{overlay.Name}");
			}

			ButtonContainer.Create(close, UIAnchor.FullStretch, Offset.zero)
				.WithColor(Oxide.Ext.Chaos.UIFramework.Color.Clear)
				.WithCallback(CallbackHandler, _ => ToggleOverlayState(mapUser, overlay.Name),
					mapUser.Player.UserIDString + ".toggle." + overlay.Name);
		});
	}

	protected virtual UpdateComponent UpdateToggleState(BaseMapUser mapUser, Overlay overlay, bool isActive)
	{
		if (!string.IsNullOrEmpty(overlay.ToggleIcon))
		{
			var update = ChaosUI.PrepareUpdate<ImageComponent>($"ui.{PluginIdentifier}.toggle.{overlay.Name}");
			update.Component.Color = isActive ? TextColor : BorderColor;
			return update;
		}
		if (!string.IsNullOrEmpty(overlay.TogglePng))
		{
			var update = ChaosUI.PrepareUpdate<RawImageComponent>($"ui.{PluginIdentifier}.toggle.{overlay.Name}");
			update.Component.Color = isActive ? TextColor : BorderColor;
			return update;
		}
		return null;
	}

	protected void RenderOverlayToggles(BaseMapUser mapUser, string parent)
	{
		if (mapUser?.Player == null)
			return;

		_overlayBuffer.Clear();
		for (int i = 0; i < Overlays.Count; i++)
		{
			Overlay overlay = Overlays[i];
			if (overlay.IsToggleable && overlay.CanViewOverlay != null && overlay.CanViewOverlay(mapUser))
				_overlayBuffer.Add(overlay);
		}

		if (_overlayBuffer.Count == 0)
			return;

		float width = _overlayBuffer.Count * 20f + (_overlayBuffer.Count + 1) * 2f;
		BaseContainer container = CreateToggleContainer(width, 24f, parent);
		if (container == null)
			return;

		BaseMapUser captured = mapUser;
		container.WithChildren(toggles =>
		{
			for (int i = 0; i < _overlayBuffer.Count; i++)
			{
				Overlay overlay = _overlayBuffer[i];
				float x = 2f + i * 22f;
				bool state = captured.GetOverlayState(overlay.Name);
				RenderToggle(captured, toggles, new Offset(x, 2f, x + 20f, 22f), overlay, state);
			}
		}).DestroyExisting();

		ChaosUI.Show(mapUser.Player, container);
	}

	protected void RenderOverlays(BaseMapUser mapUser, string parent)
	{
		if (mapUser?.Player == null)
			return;

		for (int i = 0; i < Overlays.Count; i++)
		{
			Overlay overlay = Overlays[i];
			if (overlay.CanViewOverlay != null && !overlay.CanViewOverlay(mapUser))
				continue;
			if (overlay.IsToggleable && !mapUser.GetOverlayState(overlay.Name))
				continue;
			overlay.RenderOverlay?.Invoke(mapUser, $"ui.{PluginIdentifier}.overlay.{overlay.Name}", parent, mapUser.GetOverlayState(overlay.Name));
		}
	}

	public static (int x, int y) GetFogCellCoordinates(int index)
	{
		int x = index / FogCellsPerRow;
		int y = (FogCellsPerRow - 1) - index % FogCellsPerRow;
		return (x, y);
	}

	protected bool ShouldRenderFogOfWar(BasePlayer.FogMode fogMode)
	{
		if (fogMode == BasePlayer.FogMode.Mainland)
			return _mapConfig != null && _mapConfig.RenderFogOfWar && Server.fogofwar;
		if (_deepSeaMapConfig == null || !_deepSeaMapConfig.RenderFogOfWar)
			return false;
		return Server.deepSeaFogofwar;
	}

	protected UIAnchor CalculateFogOverlaySize(BasePlayer.FogMode fogMode)
	{
		float size = fogMode == BasePlayer.FogMode.Mainland
			? World.Size
			: DeepSeaManager.DeepSeaBounds.size.x;
		float num2 = size + (_mapConfig?.OceanMargin ?? 500) * 2f;
		float num5 = 6000f / num2 * 0.5f;
		return new UIAnchor(0f - num5, 0f - num5, 1f + num5, 1f + num5);
	}

	protected void OnClearForOfWar(BaseMapUser mapUser, bool mainland, bool deepSea)
	{
		BasePlayer player = mapUser?.Player;
		if (!player)
			return;

		BasePlayer.FogMode current = player.CurrentFogMode;
		if (!ShouldRenderFogOfWar(current) || !player.ShouldRunFogOfWar)
			return;
		if (!(current == BasePlayer.FogMode.Mainland && mainland) &&
		    !(current == BasePlayer.FogMode.DeepSea && deepSea))
			return;

		for (int i = 0; i < FogCellsTotal; i++)
		{
			(int x, int y) = GetFogCellCoordinates(i);
			EnqueueFogCellUpdate(mapUser, current, x, y, null);
		}
	}

	protected void OnEnterExitDeepSea(BaseMapUser mapUser)
	{
		if (!ShouldRenderFogOfWar(BasePlayer.FogMode.Mainland) && !ShouldRenderFogOfWar(BasePlayer.FogMode.DeepSea))
			return;
		if (mapUser?.Player == null)
			return;

		BasePlayer.FogMode current = mapUser.Player.CurrentFogMode;
		if (mapUser.CurrentFogMode == current)
			return;

		mapUser.BuildFullFogTexture(this, current);
		SendFogOfWarTransformUpdate(mapUser, CalculateFogOverlaySize(current));
	}

	public void SendFogOfWarUpdate(BaseMapUser mapUser, uint crc)
	{
		BasePlayer player = mapUser?.Player;
		if (!player)
			return;

		var update = ChaosUI.PrepareUpdate<ImageComponent>(string.Format(FogOverlay, PluginIdentifier));
		update.Component.PNG = crc.ToString();
		update.Component.Color = player.ShouldRunFogOfWar ? FogColor : Oxide.Ext.Chaos.UIFramework.Color.Clear;
		update.MarkFieldsDirty(nameof(ImageComponent.PNG));
		update.Send(player);
	}

	protected void SendFogOfWarTransformUpdate(BaseMapUser mapUser, UIAnchor anchor)
	{
		BasePlayer player = mapUser?.Player;
		if (!player)
			return;

		var update = ChaosUI.PrepareUpdate<RectTransformComponent>(string.Format(FogOverlay, PluginIdentifier));
		update.Component.Set(anchor, Offset.zero);
		update.MarkFieldsDirty(nameof(RectTransformComponent.AnchorMin));
		update.MarkFieldsDirty(nameof(RectTransformComponent.AnchorMax));
		update.Send(player);
	}

	protected void EnqueueFogCellUpdate(BaseMapUser mapUser, BasePlayer.FogMode fogMode, int x, int y, byte[] bytes)
	{
		_fogCellUpdateQueue.Enqueue((mapUser, fogMode, x, y, bytes));
		if (_isProcessingFogCellQueue)
			return;
		_isProcessingFogCellQueue = true;
		if (ServerMgr.Instance != null)
			ServerMgr.Instance.Invoke(ProcessFogCellQueue, 0f);
		else
			ProcessFogCellQueue();
	}

	private void ProcessFogCellQueue()
	{
		if (_fogCellUpdateQueue.Count == 0)
		{
			_isProcessingFogCellQueue = false;
			return;
		}

		var (mapUser, fogMode, x, y, bytes) = _fogCellUpdateQueue.Dequeue();
		if (mapUser?.Player != null && fogMode == mapUser.Player.CurrentFogMode)
		{
			if (bytes != null)
				mapUser.UpdateFogCell(this, x, y, bytes, sendUpdate: true);
			else
				mapUser.BuildFullFogTexture(this, fogMode);
		}

		if (_fogCellUpdateQueue.Count > 0)
			ServerMgr.Instance?.Invoke(ProcessFogCellQueue, 0f);
		else
			_isProcessingFogCellQueue = false;
	}

	public abstract BaseMapUser FindMapUser(BasePlayer player);

	protected void RegisterOverlay(Overlay overlay)
	{
		if (Overlays.Contains(overlay))
			return;
		Overlays.Add(overlay);
		Overlays.Sort((a, b) => a.Priority.CompareTo(b.Priority));
		onAvailableOverlaysChanged?.Invoke();
	}

	protected void UnregisterOverlay(string name)
	{
		for (int i = 0; i < Overlays.Count; i++)
		{
			if (Overlays[i].Name != name)
				continue;
			Overlays.RemoveAt(i);
			onAvailableOverlaysChanged?.Invoke();
			return;
		}
	}

	protected void OnOverlayChanged(string name)
	{
		for (int i = 0; i < Overlays.Count; i++)
		{
			if (Overlays[i].Name != name)
				continue;
			onOverlayUpdated?.Invoke(Overlays[i]);
			return;
		}
	}

	internal void ToggleOverlayState(BaseMapUser mapUser, string name)
	{
		if (mapUser?.Player == null)
			return;
		bool state = !mapUser.GetOverlayState(name);
		mapUser.SetOverlayState(name, state);
		OnOverlayStateChanged(mapUser, name, state);
	}

	private void OnOverlayStateChanged(BaseMapUser mapUser, string name, bool state)
	{
		for (int i = 0; i < Overlays.Count; i++)
		{
			Overlay overlay = Overlays[i];
			if (overlay.Name != name)
				continue;

			_updateBuffer.Clear();
			UpdateComponent overlayUpdate = overlay.UpdateOverlayState?.Invoke(mapUser, $"ui.{PluginIdentifier}.overlay.{overlay.Name}", state);
			UpdateComponent toggleUpdate = UpdateToggleState(mapUser, overlay, state);
			if (overlayUpdate != null) _updateBuffer.Add(overlayUpdate);
			if (toggleUpdate != null) _updateBuffer.Add(toggleUpdate);
			if (_updateBuffer.Count > 0)
				ChaosUI.SendUpdates(mapUser.Player, _updateBuffer);
			return;
		}
	}

	protected void OnOverlayUpdated(BaseMapUser mapUser, Overlay overlay)
	{
		if (!mapUser.GetOverlayState(overlay.Name))
			return;
		UpdateComponent update = overlay.UpdateOverlayState?.Invoke(mapUser, $"ui.{PluginIdentifier}.overlay.{overlay.Name}", true);
		update?.Send(mapUser.Player);
	}
}
