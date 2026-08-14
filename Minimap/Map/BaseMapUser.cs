using System;
using System.Collections.Generic;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Plugins;
using ProtoBuf;
using Unity.Collections;
using UnityEngine;

namespace Oxide.Ext.Chaos.Map;

public abstract class BaseMapUser
{
	public bool IsActive;

	public Hash<string, bool> Overlays = new Hash<string, bool>();

	[JsonIgnore]
	public BasePlayer Player;

	[JsonIgnore]
	private Texture2D _fogTexture;

	[JsonIgnore]
	private NativeArray<Color32> _fogPixels;

	[JsonIgnore]
	private BasePlayer.FogMode _currentFogMode;

	[JsonIgnore]
	private uint _mainlandFogCrc;

	[JsonIgnore]
	private uint _deepSeaFogCrc;

	[JsonIgnore]
	private static Texture2D _fogBuffer;

	private const int FogResolution = 256;

	[JsonIgnore]
	public BasePlayer.FogMode CurrentFogMode => _currentFogMode;

	[JsonIgnore]
	public uint CurrentFogCrc
	{
		get => _currentFogMode == BasePlayer.FogMode.Mainland ? _mainlandFogCrc : _deepSeaFogCrc;
		private set
		{
			if (_currentFogMode == BasePlayer.FogMode.Mainland)
				_mainlandFogCrc = value;
			else
				_deepSeaFogCrc = value;
		}
	}

	public BaseMapUser()
	{
	}

	public BaseMapUser(BasePlayer player)
	{
		Player = player;
	}

	public bool GetOverlayState(string name)
	{
		return Overlays.TryGetValue(name, out var value) && value;
	}

	public void SetOverlayState(string name, bool state)
	{
		Overlays[name] = state;
	}

	private List<uint> GetFogImageList(BasePlayer.FogMode mode)
	{
		if (!Player)
			return null;

		PlayerState state = Player.State;
		if (mode == BasePlayer.FogMode.Mainland)
		{
			if (state.fogImagesMainland == null)
				state.fogImagesMainland = Pool.Get<PooledList<uint>>();
			return Player.State.fogImagesMainland;
		}

		if (state.fogImagesDeepSea == null)
			state.fogImagesDeepSea = Pool.Get<PooledList<uint>>();
		return Player.State.fogImagesDeepSea;
	}

	public void OnPlayerDisconnected()
	{
		if (_fogTexture)
		{
			UnityEngine.Object.Destroy(_fogTexture);
			_fogTexture = null;
		}
	}

	public void BuildFullFogTexture(ChaosMapPlugin plugin, BasePlayer.FogMode fogMode)
	{
		_currentFogMode = fogMode;
		if (!_fogTexture)
		{
			_fogTexture = new Texture2D(FogResolution, FogResolution, TextureFormat.RGBA32, mipChain: false);
			_fogPixels = _fogTexture.GetRawTextureData<Color32>();
		}

		List<uint> fogImageList = GetFogImageList(_currentFogMode);
		if (fogImageList == null)
			return;

		for (int i = 0; i < fogImageList.Count; i++)
		{
			(int x, int y) = ChaosMapPlugin.GetFogCellCoordinates(i);
			uint crc = fogImageList[i];
			byte[] bytes = null;
			if (crc != 0 && Player.net != null)
			{
				bytes = FileStorage.server.Get(crc, FileStorage.Type.png, Player.net.ID,
					(uint)(fogMode == BasePlayer.FogMode.DeepSea ? 16 + i : i));
			}
			UpdateFogCell(plugin, x, y, bytes, sendUpdate: false);
		}

		ApplyAndStoreFogTexture();
		plugin.SendFogOfWarUpdate(this, CurrentFogCrc);
	}

	public void UpdateFogCell(ChaosMapPlugin plugin, int x, int y, byte[] bytes, bool sendUpdate)
	{
		int originX = x * 64;
		int originY = y * 64;

		if (bytes != null)
		{
			if (!_fogBuffer)
				_fogBuffer = new Texture2D(64, 64);
			_fogBuffer.LoadImage(bytes);
			Color32[] pixels = _fogBuffer.GetPixels32();
			for (int i = 0; i < pixels.Length; i++)
			{
				int localX = i % 64;
				int localY = i / 64;
				int px = originX + localX;
				int py = originY + localY;
				Color32 value = pixels[i];
				byte inverted = (byte)(255 - value.r);
				value.r = inverted;
				value.g = inverted;
				value.b = inverted;
				value.a = byte.MaxValue;
				_fogPixels[py * FogResolution + px] = value;
			}
		}
		else
		{
			Color32 white = new Color32(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue);
			for (int i = 0; i < 4096; i++)
			{
				int localX = i % 64;
				int localY = i / 64;
				_fogPixels[(originY + localY) * FogResolution + (originX + localX)] = white;
			}
		}

		if (sendUpdate)
		{
			ApplyAndStoreFogTexture();
			plugin.SendFogOfWarUpdate(this, CurrentFogCrc);
		}
	}

	private void ApplyAndStoreFogTexture()
	{
		if (!_fogTexture || Player == null || Player.net == null)
			return;

		_fogTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
		byte[] data = _fogTexture.EncodeToPNG();
		uint previous = CurrentFogCrc;
		if (previous != 0)
		{
			try { FileStorage.server.Remove(previous, FileStorage.Type.png, Player.net.ID); }
			catch { }
		}

		CurrentFogCrc = FileStorage.server.Store(data, FileStorage.Type.png, Player.net.ID);
	}
}
