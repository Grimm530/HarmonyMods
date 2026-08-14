using Facepunch;
using Newtonsoft.Json;
using Oxide.Ext.Chaos;
using Oxide.Ext.Chaos.Data;
using Oxide.Ext.Chaos.Json;
using Oxide.Ext.Chaos.Map;
using Oxide.Ext.Chaos.UIFramework;
using Oxide.Plugins;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

using Chaos = Oxide.Ext.Chaos;
using Color = UnityEngine.Color;
using Debug = UnityEngine.Debug;
using Layer = Oxide.Ext.Chaos.UIFramework.Layer;
using Time = UnityEngine.Time;
using UIAnchor = Oxide.Ext.Chaos.UIFramework.Anchor;

namespace MinimapHarmony
{
    [Info("Minimap", "k1lly0u", "1.3.1")]
    public class Minimap : ChaosMapPlugin
    {
        public override string Title => "Minimap";

        #region Fields

        private bool _isPluginReady;
        private HeatMapService _heatMap;
        
        private static Datafile<Hash<ulong, MinimapUser>> _userData;
        
        private static readonly Hash<EnvironmentVolume, (DungeonBaseLink, int)> VolumeLookup = new();
        
        [Chaos.Permission]
        private const string USE_PERMISSION = "minimap.use";
        
        #endregion
        
        #region Harmony

        public void HarmonyInit()
        {
            LoadConfiguration();
            RegisterMessages();
            MinimapHost.Instance?.ReloadLanguage();
            ImageStore.LoadIndex();
            _userData = new Datafile<Hash<ulong, MinimapUser>>("minimap.users");
            MinimapUser.OnEnterExitDeepSea += OnEnterExitDeepSea;
            RegisterPermissions();
        }

        public void RegisterPermissions()
        {
            var permission = MinimapHost.Instance?.Permission;
            if (permission == null) return;
            if (!permission.PermissionExists(USE_PERMISSION, this))
                permission.RegisterPermission(USE_PERMISSION, this);
        }

        public void HarmonyServerInitialized()
        {
	        FindLabVolumes();
			SetupInterface(Configuration.Map, Configuration.Map.DeepSea);
		}

        public void OnPlayerConnected(BasePlayer player) => QueueMinimap(player);

        public void OnPlayerEnteredGame(BasePlayer player)
        {
	        if (player != null && !player.IsSleeping())
		        QueueMinimap(player, 0.5f);
        }

        public void OnPlayerSleepEnded(BasePlayer player) => QueueMinimap(player, 0.35f);

        private void QueueMinimap(BasePlayer player, float delay = 0.25f, int attempt = 0)
        {
	        if (player == null || player.IsDestroyed)
		        return;

	        var mgr = ServerMgr.Instance;
	        if (mgr == null)
		        return;

	        mgr.Invoke(() =>
	        {
		        if (player == null || player.IsDestroyed || !player.IsConnected)
			        return;

		        if (!_isPluginReady || player.IsReceivingSnapshot || player.IsSleeping())
		        {
			        if (attempt < 40)
				        QueueMinimap(player, 0.5f, attempt + 1);
			        else if (!_isPluginReady)
				        Debug.LogWarning("[Minimap] Timed out waiting for map images before showing UI");
			        return;
		        }

		        TryShowMinimap(player);
	        }, delay);
        }

        private void TryShowMinimap(BasePlayer player)
        {
	        if (player == null || player.IsDestroyed || !player.IsConnected)
		        return;

	        bool wasActive = _userData.Data.TryGetValue(player.userID.Get(), out MinimapUser mapUser) && mapUser.IsActive;
	        bool byDefault = Configuration.Map.EnabledByDefault && player.HasPermission(USE_PERMISSION);
	        if (!wasActive && !byDefault)
		        return;

	        MapManager.Register(player);
        }

        public void OnDeepSeaOpened(DeepSeaManager manager)
        {
	        if (!_isPluginReady)
		        return;

	        ServerMgr.Instance.StartCoroutine(RunLogged(GenerateDeepSeaMap(), "deep sea map layers"));
        }
        
        public void OnFogOfWarImageUpdate(BasePlayer player)
        {
            if (!ShouldRenderFogOfWar(BasePlayer.FogMode.Mainland) && !ShouldRenderFogOfWar(BasePlayer.FogMode.DeepSea))
                return;

            BaseMapUser mapUser = FindMapUser(player);
            if (mapUser == null)
                return;

            EnqueueFogCellUpdate(mapUser, player.CurrentFogMode, 0, 0, null);
        }
        
        public void OnClearFogOfWar(BasePlayer player, bool mainland, bool deepSea)
        {
            if (!ShouldRenderFogOfWar(BasePlayer.FogMode.Mainland) && !ShouldRenderFogOfWar(BasePlayer.FogMode.DeepSea))
                return;

            BaseMapUser mapUser = FindMapUser(player);
            if (mapUser == null)
                return;
	        
            OnClearForOfWar(mapUser, mainland, deepSea);
        }

        public void OnFogOfWarStale(BasePlayer player)
        {
            if (!ShouldRenderFogOfWar(BasePlayer.FogMode.Mainland) && !ShouldRenderFogOfWar(BasePlayer.FogMode.DeepSea))
                return;
            
            BaseMapUser mapUser = FindMapUser(player);
            if (mapUser == null)
                return;
	        
            OnClearForOfWar(mapUser, true, true);
        }

        public void OnServerSave()
        {
	        _userData?.Save();
	        _heatMap?.OnServerSave();
        }

        public void OnPvpDeath(BasePlayer victim, HitInfo info)
        {
	        _heatMap?.OnPvpDeath(victim, info);
        }

        public void PrepareHeatUser(BaseMapUser mapUser)
        {
	        _heatMap?.PrepareUser(mapUser);
        }

        internal void RegisterHeatOverlay()
        {
	        RegisterOverlay(new Overlay
	        {
		        Name = HeatMapService.OverlayName,
		        IsToggleable = true,
		        Priority = 0,
		        ToggleIcon = Icon.Icons_Fire,
		        CanViewOverlay = user => user?.Player != null && !user.Player.IsDestroyed,
		        RenderOverlay = (user, name, parent, isActive) =>
			        _heatMap?.RenderOverlayUi(user, name, parent, isActive),
		        UpdateOverlayState = (user, name, isActive) =>
			        _heatMap?.UpdateOverlayUi(user, name, isActive)
	        });
        }

        internal void NotifyHeatOverlayChanged()
        {
	        OnOverlayChanged(HeatMapService.OverlayName);
        }

        internal void UnregisterHeatOverlay()
        {
	        UnregisterOverlay(HeatMapService.OverlayName);
        }

        internal void StoreHeatImage(string name, Color32[] src, Texture2D texture, Action<string> callback)
        {
	        texture.SetPixels32(src);
	        texture.Apply(updateMipmaps: false);
	        StoreImageData(name, texture.EncodeToPNG(), true, callback);
        }

        public static string CurrentWorldIdForHeat(int renderResolution)
        {
	        return CurrentWorldID(renderResolution);
        }

        public void OnPlayerDisconnected(BasePlayer player)
        {
	        MinimapUser mapUser = MapManager.FindUser(player);
	        mapUser?.OnPlayerDisconnected();
        }

        public void HarmonyUnload()
        {
	        MinimapUser.OnEnterExitDeepSea -= OnEnterExitDeepSea;
	        onAvailableOverlaysChanged -= OnAvailableOverlaysChanged;
	        onOverlayUpdated -= OnOverlayUpdated;
	        _heatMap?.Stop();
	        _heatMap = null;
	        
	        _userData?.Save();
	        
	        MapManager.Destroy();
        }
        
        #endregion
        
        #region Setup
        
        private void FindLabVolumes()
        {
	        List<EnvironmentVolume> childVolumes = Pool.Get<List<EnvironmentVolume>>();

	        foreach (DungeonBaseInfo dungeonBaseInfo in TerrainMeta.Path.DungeonBaseEntrances)
	        {
		        for (int i = 0; i < dungeonBaseInfo.Floors.Count; i++)
		        {
			        DungeonBaseFloor dungeonBaseFloor = dungeonBaseInfo.Floors[i];
			        if (dungeonBaseFloor != null)
			        {
				        int level = i;
				        foreach (DungeonBaseLink dungeonBaseLink in dungeonBaseFloor.Links)
				        {
					        if (dungeonBaseLink.MapRendererLods == null || dungeonBaseLink.MapRendererLods.Length == 0)
						        continue;

					        dungeonBaseLink.GetComponentsInChildren<EnvironmentVolume>(childVolumes);

					        foreach (EnvironmentVolume environmentVolume in childVolumes)
						        VolumeLookup[environmentVolume] = (dungeonBaseLink, level);

					        childVolumes.Clear();
				        }
			        }
		        }
	        }
        }
        
        protected override void SetupInterface(MapConfig mapConfig, DeepSeaMapConfig deepSeaConfig)
        {
	        base.SetupInterface(mapConfig, deepSeaConfig);
	        
	        onAvailableOverlaysChanged += OnAvailableOverlaysChanged;
	        onOverlayUpdated += OnOverlayUpdated;
	        
	        _mapUtility = new MapImageUtility(Configuration.Map);
	        CallbackHandler = new CommandCallbackHandler(this);
	        BorderColor = Configuration.UI.BorderColor;
	        MarkerColor = Configuration.UI.MarkerColor;
	        ForegroundColor = Configuration.UI.ForegroundColor;
	        TextColor = Configuration.UI.TextColor;
	        
	        MapManager.Initialize(InitialMapRender, UpdateMapRender);

	        _heatMap = new HeatMapService(this);
	        if (HeatMapService.IsEnabled)
		        _heatMap.Start();

	        // Import arrows first so a missing FileStorage index does not force a full re-render.
	        ImportMapArrows();
	        if (HasAllImages())
	        {
		        Debug.Log("[Minimap] Using cached map images");
		        OnPluginReady();
	        }
	        else
	        {
		        Debug.Log("[Minimap] Map images missing — starting render");
		        ServerMgr.Instance.StartCoroutine(RegenerateMaps());
	        }
        }

        private void OnPluginReady()
        {
	        _isPluginReady = true;
	        Debug.Log("[Minimap] Ready");

	        foreach (BasePlayer player in BasePlayer.activePlayerList)
		        OnPlayerConnected(player);
        }
        
        #endregion
        
        #region ImageLibrary
        
        private static string _currentWorldID;

        private bool HasAllImages()
        {
	        _currentWorldID = CurrentWorldID(Configuration.Map.RenderResolution);

	        //if (!HasEmptyFogImage())
		        //return false;
	        
	        if (!HasArrowImages())
		        return false;
	        
	        if (!TryGetImage(string.Format(WorldMap, _currentWorldID), out string overworld))
		        return false;
	        
	        _mapLayers[MapLayer.Overworld] = overworld;

	        if (Configuration.Map.RenderMonumentNames || Configuration.Map.RenderTunnelEntrances)
	        {
		        if (!TryGetImage(string.Format(OverworldMarkerMap, _currentWorldID), out string overworldMarkers))
			        return false;

		        _mapLayers[MapLayer.OverworldMarkers] = overworldMarkers;

		        if (!TryGetImage(string.Format(UnderworldMarkerMap, _currentWorldID), out string underworldMarkers))
			        return false;

		        _mapLayers[MapLayer.UnderworldMarkers] = underworldMarkers;
	        }

	        // Deep sea manager is destroyed when ConVar DeepSea.enabled is false.
	        if (IsDeepSeaOpen())
	        {
		        if (!TryGetImage(string.Format(DeepSeaMap, _currentWorldID), out string deepSea))
			        return false;

		        _mapLayers[MapLayer.DeepSea] = deepSea;

		        if (Configuration.Map.DeepSea.RenderDeepSeaMarkers)
		        {
			        if (!TryGetImage(string.Format(DeepSeaMarkerMap, _currentWorldID), out string deepSeaMarkers))
				        return false;

			        _mapLayers[MapLayer.DeepSeaMarkers] = deepSeaMarkers;
		        }
	        }

	        if (Configuration.Map.EnableTunnels)
	        {
		        if (!TryGetImage(string.Format(TunnelMap, _currentWorldID), out string tunnels))
			        return false;
		        
		        _mapLayers[MapLayer.TrainTunnels] = tunnels;
	        }

	        if (Configuration.Map.EnableLabs)
	        {
		        for (MapLayer i = MapLayer.Underwater1; i < MapLayer.Underwater8; i++)
		        {
			        if (!TryGetImage(string.Format(LabMap, _currentWorldID, (int)i - 1), out string lab))
				        return false;
			        
			        _mapLayers[i] = lab;
		        }
	        }

	        return true;
        }
        
        #endregion
        
        #region Map Generation
        
        private const string WorldMap = "world.minimap.{0}";
        private const string DeepSeaMap = "world.deepsea.minimap.{0}";
        private const string TunnelMap = "world.tunnel.minimap.v2.{0}";
        private const string LabMap = "world.lab.minimap.v2.{0}.{1}";
        private const string OverworldMarkerMap = "world.overworld.markers.v2.{0}";
        private const string UnderworldMarkerMap = "world.underworld.markers.v2.{0}";
        private const string DeepSeaMarkerMap = "world.deepsea.markers.v2.{0}";
        
        private int _pendingImages = 0;
        private int _imagesStored = 0;

        private static bool IsDeepSeaOpen()
        {
	        try
	        {
		        var instance = DeepSeaManager.ServerInstance;
		        return instance != null && instance.IsOpen();
	        }
	        catch
	        {
		        return false;
	        }
        }
        
        private IEnumerator RegenerateMaps()
        {
	        _pendingImages = 1 + (Configuration.Map.EnableTunnels ? 1 : 0) + (Configuration.Map.EnableLabs ? 8 : 0);
	        if (Configuration.Map.RenderMonumentNames || Configuration.Map.RenderTunnelEntrances)
		        _pendingImages += 2;
	        if (IsDeepSeaOpen())
		        _pendingImages += Configuration.Map.DeepSea.RenderDeepSeaMarkers ? 2 : 1;

	        Debug.Log($"[Minimap] Regenerating {_pendingImages} map layer(s) (deep sea open={IsDeepSeaOpen()})");

	        yield return RunLogged(GenerateMaps(), "world map layers");

	        if (IsDeepSeaOpen())
		        yield return RunLogged(GenerateDeepSeaMap(), "deep sea map layers");
        }

        private static IEnumerator RunLogged(IEnumerator inner, string label)
        {
	        while (true)
	        {
		        bool moved;
		        try
		        {
			        moved = inner.MoveNext();
		        }
		        catch (Exception ex)
		        {
			        Debug.LogError($"[Minimap] Render failed ({label}): {ex}");
			        yield break;
		        }

		        if (!moved)
			        yield break;

		        yield return inner.Current;
	        }
        }
        
        private IEnumerator GenerateMaps()
        {
            Debug.Log("[Minimap] Rendering world map layers");
            
            using (MapRenderer renderer = new MapRenderer(_mapUtility, Configuration.Map, Configuration.Map.DeepSea))
            {
	            int renderRes = Configuration.Map.RenderResolution;//8471

	            NativeArray<Color> buffer0 = new NativeArray<Color>(renderRes * renderRes, Allocator.Persistent);
	            NativeArray<Color> buffer1 = new NativeArray<Color>(renderRes * renderRes, Allocator.Persistent);

	            Texture2D texture2D = new Texture2D(renderRes, renderRes, TextureFormat.RGBA32, false);

	            if (Configuration.Map.RenderMonumentNames || Configuration.Map.RenderTunnelEntrances)
	            {
		            Debug.Log("[Minimap] Rendering map markers and monument names");
		            
		            yield return renderer.DrawMonumentMarkers(buffer0, false);

		            StoreImageData(string.Format(OverworldMarkerMap, _currentWorldID), buffer0, texture2D, true, (png) => OnImageStored(MapLayer.OverworldMarkers, png));

		            yield return renderer.DrawMonumentMarkers(buffer0, true);

		            StoreImageData(string.Format(UnderworldMarkerMap, _currentWorldID), buffer0, texture2D, true, (png) => OnImageStored(MapLayer.UnderworldMarkers, png));
	            }

	            Debug.Log("[Minimap] Rendering overworld map layer");
	            
	            // Send both buffers, buffer0 will contain the base overworld map image to be used as the underlay
	            // for the other layers, buffer1 will contain the tinted overworld map image
	            yield return renderer.RenderOverworld(buffer0, buffer1, false);
	            
	            StoreImageData(string.Format(WorldMap, _currentWorldID), buffer1, texture2D, false, (png) => OnImageStored(MapLayer.Overworld, png));

	            if (Configuration.Map.EnableTunnels || Configuration.Map.EnableLabs)
	            {
		            // Overlay overworld map image with underworld color
		            renderer.BlendUnderworldOverlay(buffer0);
		            yield return CoroutineEx.waitForEndOfFrame;

		            // Copy results to buffer1
		            buffer0.CopyTo(buffer1);

		            // Render tunnel layers
		            if (Configuration.Map.EnableTunnels)
		            {
			            Debug.Log("[Minimap] Rendering tunnel map layer");
			            yield return renderer.RenderTrainTunnels(buffer1, false);
			            StoreImageData(string.Format(TunnelMap, _currentWorldID), buffer1, texture2D, false, (png) => OnImageStored(MapLayer.TrainTunnels, png));
		            }

		            // Render lab layers
		            if (Configuration.Map.EnableLabs)
		            {
			            const int UNDERWATER_LAB_COUNT = 8;
			            for (int i = 0; i < UNDERWATER_LAB_COUNT; i++)
			            {
				            MapLayer mapLayer = MapLayer.Underwater1 + i;
				            // Copy results from overworld to buffer1
				            buffer0.CopyTo(buffer1);
				            
				            Debug.Log($"[Minimap] Rendering lab map layer {i}");
				            yield return renderer.RenderUnderwaterLabs(buffer1, i, false);
				            StoreImageData(string.Format(LabMap, _currentWorldID, i), buffer1, texture2D, false, (png) => OnImageStored(mapLayer, png));
			            }
		            }
	            }

	            buffer0.Dispose();
	            buffer1.Dispose();
            }

            Debug.Log("[Minimap] Finished rendering world map layers");
        }
        
        private IEnumerator GenerateDeepSeaMap()
        {
            Debug.Log("[Minimap] Rendering deep sea world map layer");
            
            using (MapRenderer renderer = new MapRenderer(_mapUtility, Configuration.Map, Configuration.Map.DeepSea))
            {
	            int renderRes = Configuration.Map.RenderResolution;//8471

	            NativeArray<Color> buffer0 = new NativeArray<Color>(renderRes * renderRes, Allocator.Persistent);
	            NativeArray<Color> buffer1 = new NativeArray<Color>(renderRes * renderRes, Allocator.Persistent);

	            Texture2D texture2D = new Texture2D(renderRes, renderRes, TextureFormat.RGBA32, false);

	            if (Configuration.Map.DeepSea.RenderDeepSeaMarkers)
	            {
		            Debug.Log("[Minimap] Rendering deep sea map markers and monument names");
		            
		            yield return renderer.DrawDeepSeaMonumentMarkers(buffer0);

		            StoreImageData(string.Format(DeepSeaMarkerMap, _currentWorldID), buffer0, texture2D, true, (png) => OnImageStored(MapLayer.DeepSeaMarkers, png));
	            }

	            Debug.Log("[Minimap] Rendering deep sea map layer");
	            
	            yield return renderer.RenderDeepSea(buffer0, buffer1, false);
	            
	            StoreImageData(string.Format(DeepSeaMap, _currentWorldID), buffer1, texture2D, false, (png) => OnImageStored(MapLayer.DeepSea, png));
	            
	            buffer0.Dispose();
	            buffer1.Dispose();
            }

            Debug.Log("[Minimap] Finished rendering deep sea world map layers");
        }

        private void OnImageStored(MapLayer mapLayer, string png)
        {
	        if (string.IsNullOrEmpty(png))
		        Debug.LogWarning($"[Minimap] Stored empty image for {mapLayer}");
	        else
		        _mapLayers[mapLayer] = png;

	        _imagesStored++;
	        if (_imagesStored == _pendingImages)
		        OnPluginReady();
        }
        
        #endregion
        
        #region Map Manager

        private const string UI_MINIMAP = "ui.minimap";
        private const string UI_MOUSE_HELPER_PARENT = "ui.minimap.mouse1";
        private const string UI_MOUSE_HELPER = "ui.minimap.mouse2";
        private const string UI_MOUSE_OVERLAY_PARENT = "ui.minimap.mouse3";
        private const string UI_MOUSE_OVERLAY = "ui.minimap.mouse4";
        private const string UI_MINIMAP_SETTINGS = "ui.minimap.settings";
        private const string UI_PLAYER_POSITION_MARKER = "ui.minimap.player.position";
        private const string UI_PLAYER_ROTATION_MARKER = "ui.minimap.player.rotation";
        private const string UI_MAP_IMAGE = "ui.minimap.image";
        private const string UI_SCROLLVIEW = "ui.minimap.scrollview";
        private const string UI_MARKER_OVERLAY = "ui.minimap.markers";
        
        private const string UI_OVERLAYS = "ui.minimap.overlays";
        
        private static MapImageUtility _mapUtility;
        
        private static ScrollViewComponent _scrollView = new()
        {
	        Horizontal = true,
	        Vertical = true,
	        MovementType = ScrollRect.MovementType.Clamped
        };

        private static Hash<MapLayer, string> _mapLayers = new();
        
        private readonly List<UpdateComponent> _updates = new();
        
        public enum MapLayer
        {
	        DeepSeaMarkers = -4,
	        UnderworldMarkers = -3, 
	        OverworldMarkers = -2,
	        Overworld = -1, 
	        TrainTunnels = 0,
	        Underwater1 = 1,
	        Underwater2 = 2,
	        Underwater3 = 3,
	        Underwater4 = 4,
	        Underwater5 = 5,
	        Underwater6 = 6,
	        Underwater7 = 7,
	        Underwater8 = 8,
	        Dungeons = 10, 
	        DeepSea = 11,
        }

        public override BaseMapUser FindMapUser(BasePlayer player) => MapManager.FindUser(player);

        private class MapManager : MonoBehaviour
        {
	        private static MapManager _instance;

	        private Queue<MinimapUser> _activeQueue = new();
	        
	        private Queue<MinimapUser> _nextQueue = new();

	        private readonly Stopwatch _stopwatch = new();
	        
	        private Action<MinimapUser> _initialMapRender;
	        private Action<MinimapUser, Vector3?, float?, MapLayer?> _updateMapRender;

	        public static void Initialize(Action<MinimapUser> initialMapRender, Action<MinimapUser, Vector3?, float?, MapLayer?> updateMapRender)
	        {
		        if (_instance)
			        return;
		        
		        _instance = new GameObject("MapManager").AddComponent<MapManager>();
		        
		        _instance._initialMapRender = initialMapRender;
		        _instance._updateMapRender = updateMapRender;
	        }
	        
	        public static void Destroy()
	        {
		        if (!_instance)
			        return;
		        
		        foreach (MinimapUser mapUser in _instance._activeQueue)
		        {
			        if (mapUser.Player)
			        {
				        ChaosUI.Destroy(mapUser.Player, UI_MINIMAP);
				        ChaosUI.Destroy(mapUser.Player, UI_MINIMAP_SETTINGS);
				        ChaosUI.Destroy(mapUser.Player, UI_MOUSE_HELPER);
			        }
			        
			        mapUser.OnPlayerDisconnected();
		        }

		        foreach (MinimapUser mapUser in _instance._nextQueue)
		        {
			        if (mapUser.Player)
			        {
				        ChaosUI.Destroy(mapUser.Player, UI_MINIMAP);
				        ChaosUI.Destroy(mapUser.Player, UI_MINIMAP_SETTINGS);
				        ChaosUI.Destroy(mapUser.Player, UI_MOUSE_HELPER);
			        }
			        
			        mapUser.OnPlayerDisconnected();
		        }

		        _instance._activeQueue.Clear();
		        _instance._nextQueue.Clear();
		        
		        Destroy(_instance.gameObject);
		        
		        _instance = null;
	        }
	        
	        public static void Register(BasePlayer player)
	        {
		        if (!_instance)
		        {
			        Debug.LogWarning("[Minimap] MapManager not initialized — cannot show UI");
			        return;
		        }

		        if (!_userData.Data.TryGetValue(player.userID.Get(), out MinimapUser mapUser))
			        mapUser = _userData.Data[player.userID.Get()] = new MinimapUser(Mathf.Clamp(Configuration.Map.DefaultZoomLevel, 0, Configuration.Map.ZoomLevels));

		        mapUser.Player = player;
		        mapUser.IsActive = true;
		        MinimapHarmonyMod.Instance?.Plugin?.PrepareHeatUser(mapUser);

		        _instance._initialMapRender(mapUser);

		        if (!IsQueued(mapUser))
			        _instance._nextQueue.Enqueue(mapUser);
	        }

	        private static bool IsQueued(MinimapUser mapUser)
	        {
		        if (!_instance || mapUser == null)
			        return false;
		        foreach (var queued in _instance._activeQueue)
		        {
			        if (queued == mapUser)
				        return true;
		        }
		        foreach (var queued in _instance._nextQueue)
		        {
			        if (queued == mapUser)
				        return true;
		        }
		        return false;
	        }
	        
	        private void Update() => RunQueue(0.5);

	        private void RunQueue(double maximumMilliseconds)
	        {
		        _stopwatch.Restart();
		        while (_activeQueue.Count > 0)
		        {
			        if (_stopwatch.Elapsed.TotalMilliseconds >= maximumMilliseconds)
				        break;
			        
			        MinimapUser mapUser = _activeQueue.Dequeue();
			        if (mapUser == null || !mapUser.Player || !mapUser.IsActive)
				        continue;
			        
			        RunUpdateJob(mapUser);
		        }

		        CheckFlipQueue();
	        }

	        private void CheckFlipQueue()
	        {
		        if (_activeQueue.Count == 0 && _nextQueue.Count > 0)
			        (_activeQueue, _nextQueue) = (_nextQueue, _activeQueue);
	        }

	        private void RunUpdateJob(MinimapUser mapUser)
	        {
		        if (!mapUser.Player || !mapUser.IsActive)
			        return;

		        if (mapUser.ShouldUpdate(out Vector3? position, out float? rotation, out MapLayer? mapLayer) || mapUser.ForceUpdate)
			        _updateMapRender(mapUser, position, rotation, mapLayer);
		        
		        _nextQueue.Enqueue(mapUser);
	        }

	        public static IEnumerable<MinimapUser> GetActiveMapUsers()
	        {
		        if (!_instance)
			        yield break;
		        
		        foreach (MinimapUser mapUser in _instance._activeQueue)
		        {
			        if (mapUser.IsActive && mapUser.Player)
				        yield return mapUser;
		        }
		        
		        foreach (MinimapUser mapUser in _instance._nextQueue)
		        {
			        if (mapUser.IsActive && mapUser.Player)
				        yield return mapUser;
		        }
	        }

	        public static MinimapUser FindUser(BasePlayer player)
	        {
		        foreach (MinimapUser minimapUser in GetActiveMapUsers())
		        {
			        if (minimapUser.Player == player)
				        return minimapUser;
		        }

		        return null;
	        }
        }
        
        #endregion
        
        #region UI

        private void OnAvailableOverlaysChanged()
        {
	        if (!_isPluginReady)
		        return;

	        foreach (MinimapUser mapUser in MapManager.GetActiveMapUsers())
		        InitialMapRender(mapUser);
        }

        private void OnOverlayUpdated(Overlay overlay)
        {
	        if (!_isPluginReady)
		        return;

	        foreach (MinimapUser mapUser in MapManager.GetActiveMapUsers())
	        {
		        if (mapUser.GetOverlayState(overlay.Name))
			        OnOverlayUpdated(mapUser, overlay);
	        }
        }

        private void CreateMouseHelper(MinimapUser mapUser)
        {
	        if (!mapUser.Player || mapUser.MouseHelperOpen)
		        return;
	        
	        mapUser.MouseHelperOpen = true;
	        
	        BaseContainer root = ButtonContainer.Create(UI_MOUSE_HELPER, Layer.Hud, UIAnchor.Center, new Offset(2560, 1440))
		        .WithColor(Chaos.UIFramework.Color.Clear)
		        .WithCallback(CallbackHandler, arg => DestroyMouseHelper(mapUser), $"{mapUser.Player.UserIDString}.mousehelper")
		        .WithParent(UI_MOUSE_HELPER_PARENT)
		        .DestroyExisting()
		        .NeedsCursor();
	        
	        ChaosUI.Destroy(mapUser.Player, UI_MOUSE_OVERLAY);
	        ChaosUI.Show(mapUser.Player, root);
        }

        private void DestroyMouseHelper(MinimapUser mapUser)
        {
	        BaseContainer mouseOverlay = ButtonContainer.Create(UI_MOUSE_OVERLAY, Layer.Hud, UIAnchor.FullStretch, Offset.zero)
		        .WithColor(Chaos.UIFramework.Color.Clear)
		        .WithCallback(CallbackHandler, arg => CreateMouseHelper(mapUser), $"{mapUser.Player.UserIDString}.mouseoverlay")
		        .WithParent(UI_MOUSE_OVERLAY_PARENT)
		        .DestroyExisting();
	        
	        mapUser.MouseHelperOpen = false;
	        ChaosUI.Destroy(mapUser.Player, UI_MOUSE_HELPER);
	        ChaosUI.Show(mapUser.Player, mouseOverlay);
        }
	        
        private void InitialMapRender(MinimapUser mapUser)
        {
	        if (!mapUser.Player)
		        return;

	        (UIAnchor containerAnchor, Offset containerOffset, float screenSize) = mapUser.GetAnchorOffsetAndSize();
	        
	        mapUser.GetInitial(out Vector3 position, out float rotation, out MapLayer mapLayer);

	        position = mapUser.ConvertPositionToDeepSea(position);

	        Offset mapOffset = _mapUtility.CalculateImageOffsetForPosition(screenSize, position, mapUser.ZoomLevel, Configuration.Map.ZoomLevels);
	        
	        BaseContainer root = ImageContainer.Create(UI_MINIMAP, Layer.Hud, containerAnchor, containerOffset)
		        .WithColor(BorderColor)
		        .WithName(UI_MINIMAP)
		        .WithChildren(minimap =>
		        {
			        BaseContainer.Create(minimap, UIAnchor.FullStretch, Offset.zero)
				        .WithName(UI_MOUSE_HELPER_PARENT);
			        
			        // ScrollView
			        ImageContainer.Create(minimap, UIAnchor.FullStretch, new Offset(1f, 2f, -3.5f, -2f))
				        .WithColor(Chaos.UIFramework.Color.Clear)
				        .WithName(UI_SCROLLVIEW)
				        .WithScrollView(_scrollView.WithContentTransform(UIAnchor.Center, mapOffset))
				        .WithChildren(content =>
				        {
					        RawImageContainer.Create(content, UIAnchor.FullStretch, Offset.zero)
						        .WithPNG(GetMapForCurrentLayer(mapLayer))
						        .WithName(UI_MAP_IMAGE)
						        .WithChildren(image =>
						        {
							        // Third Party Overlay
							        BaseContainer.Create(image, UIAnchor.FullStretch, Offset.zero)
								        .WithName(UI_OVERLAYS);

							        if (Configuration.Map.RenderMonumentNames || Configuration.Map.RenderTunnelEntrances || Configuration.Map.DeepSea.RenderDeepSeaMarkers)
							        {
								        string markerImage = GetMarkersForCurrentLayer(mapLayer);
								        
								        RawImageContainer markers = RawImageContainer.Create(image, UIAnchor.FullStretch, Offset.zero)
									        .WithName(UI_MARKER_OVERLAY) as RawImageContainer;

								        if (!string.IsNullOrEmpty(markerImage))
									        markers.WithPNG(markerImage);
								        else markers.WithColor(Oxide.Ext.Chaos.UIFramework.Color.Clear);
							        }

							        if (ShouldRenderFogOfWar(BasePlayer.FogMode.Mainland) || ShouldRenderFogOfWar(BasePlayer.FogMode.DeepSea))
							        {
								        BasePlayer.FogMode fogMode = mapUser.Player.CurrentFogMode;
								        
								        mapUser.BuildFullFogTexture(this, fogMode);
								        
								        ImageContainer.Create(image, CalculateFogOverlaySize(fogMode), Offset.zero)
									        .WithPNG(mapUser.CurrentFogCrc.ToString())
									        .WithColor(mapUser.Player.ShouldRunFogOfWar ? FogColor : Chaos.UIFramework.Color.Clear)
									        .WithMaterial(FogMaterial)
									        .WithName(string.Format(FogOverlay, PluginIdentifier));
							        }
						        });
					        
					        float2 markerPosition = _mapUtility.WorldToImage(mapOffset, position);
					        float2 halfSize = new float2(mapOffset.Width * 0.5f, mapOffset.Height * 0.5f);
					        markerPosition = math.clamp(markerPosition, -halfSize, halfSize);

					        ImageContainer.Create(content, UIAnchor.Center, new Offset(markerPosition.x - 7, markerPosition.y - 7, markerPosition.x + 7, markerPosition.y + 7))
						        .WithSprite(Icon.Circle_Closed)
						        .WithColor(BorderColor)
						        .WithName(UI_PLAYER_POSITION_MARKER)
						        .WithChildren(player =>
						        {
							        ImageContainer.Create(player, UIAnchor.FullStretch, new Offset(1, 1, -1, -1))
								        .WithSprite(Icon.Circle_Closed)
								        .WithColor(MarkerColor)
								        .WithChildren(arrow =>
								        {
									        RawImageContainer.Create(arrow, UIAnchor.FullStretch, Offset.zero)
										        .WithPNG(GetClosestDirectionIcon(rotation))
										        .WithColor(BorderColor)
										        .WithName(UI_PLAYER_ROTATION_MARKER);
								        });
						        });
				        });
			        
			        BaseContainer.Create(minimap, UIAnchor.FullStretch, Offset.zero)
				        .WithName(UI_MOUSE_OVERLAY_PARENT)
				        .WithChildren(parent =>
				        {
					        ButtonContainer.Create(parent, UIAnchor.FullStretch, Offset.zero)
						        .WithColor(Chaos.UIFramework.Color.Clear)
						        .WithCallback(CallbackHandler, arg => CreateMouseHelper(mapUser), $"{mapUser.Player.UserIDString}.mouseoverlay")
						        .WithName(UI_MOUSE_OVERLAY);
				        });

			        // Close
			        ImageContainer.Create(minimap, UIAnchor.TopRight, new Offset(-24f, -24f, 0f, 0f))
				        .WithColor(BorderColor)
				        .WithChildren(header =>
				        {
					        ImageContainer.Create(header, UIAnchor.BottomRight, new Offset(-22f, 2f, -2f, 22f))
						        .WithColor(ForegroundColor)
						        .WithChildren(close =>
						        {
							        ImageContainer.Create(close, UIAnchor.FullStretch, new Offset(4, 4, -4, -4))
								        .WithSprite(Icon.Icons_Close)
								        .WithColor(TextColor);

							        ButtonContainer.Create(close, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Chaos.UIFramework.Color.Clear)
								        .WithCallback(CallbackHandler, arg =>
									        {
										        mapUser.IsActive = false;
										        mapUser.MouseHelperOpen = false;
										        ChaosUI.Destroy(mapUser.Player, UI_MINIMAP);
										        ChaosUI.Destroy(mapUser.Player, UI_MINIMAP_SETTINGS);
										        ChaosUI.Destroy(mapUser.Player, UI_MOUSE_HELPER);
									        }, $"{mapUser.Player.UserIDString}.collapse");
						        });
				        });
			        
			        // Settings
			        ImageContainer.Create(minimap, UIAnchor.TopLeft, new Offset(0f, -24f, 24f, 0f))
				        .WithColor(BorderColor)
				        .WithChildren(header =>
				        {
					        ImageContainer.Create(header, UIAnchor.BottomRight, new Offset(-22f, 2f, -2f, 22f))
						        .WithColor(ForegroundColor)
						        .WithChildren(close =>
						        {
							        ImageContainer.Create(close, UIAnchor.FullStretch, new Offset(4, 4, -4, -4))
								        .WithSprite(Icon.Icons_Gear)
								        .WithColor(TextColor);

							        ButtonContainer.Create(close, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Chaos.UIFramework.Color.Clear)
								        .WithCallback(CallbackHandler, arg => CreateSettingsOverlay(mapUser), $"{mapUser.Player.UserIDString}.settings");
						        });
				        });
			        
			        // Zoom
			        ImageContainer.Create(minimap, UIAnchor.BottomRight, new Offset(-46f, 0f, 0f, 24f))
				        .WithColor(BorderColor)
				        .WithChildren(zoom =>
				        {
					        ImageContainer.Create(zoom, UIAnchor.BottomRight, new Offset(-22f, 2f, -2f, 22f))
						        .WithColor(ForegroundColor)
						        .WithChildren(zoomIn =>
						        {
							        ImageContainer.Create(zoomIn, UIAnchor.FullStretch, new Offset(4, 4, -4, -4))
								        .WithSprite(Icon.Icons_Add)
								        .WithColor(TextColor);

							        ButtonContainer.Create(zoomIn, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Chaos.UIFramework.Color.Clear)
								        .WithCallback(CallbackHandler, arg =>
									        {
										        OnZoomChanged(mapUser, 1);
										        CreateMouseHelper(mapUser);
									        }, $"{mapUser.Player.UserIDString}.zoom.in");
						        });


					        ImageContainer.Create(zoom, UIAnchor.BottomRight, new Offset(-44f, 2f, -24f, 22f))
						        .WithColor(ForegroundColor)
						        .WithChildren(zoomOut =>
						        {
							        ImageContainer.Create(zoomOut, UIAnchor.FullStretch, new Offset(4, 4, -4, -4))
								        .WithSprite(Icon.Icons_Subtract)
								        .WithColor(TextColor);

							        ButtonContainer.Create(zoomOut, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Chaos.UIFramework.Color.Clear)
								        .WithCallback(CallbackHandler, arg =>
									        {
										        OnZoomChanged(mapUser, -1);
										        CreateMouseHelper(mapUser);
									        }, $"{mapUser.Player.UserIDString}.zoom.out");
						        });
				        });
		        })
		        .DestroyExisting();

	        ChaosUI.Show(mapUser.Player, root);
	        
	        RenderOverlays(mapUser, UI_OVERLAYS);
	        RenderOverlayToggles(mapUser, UI_MINIMAP);
        }
        
        private static readonly float2 VerticalMovement = new float2(0f, 10f);
        private static readonly float2 HorizontalMovement = new float2(10f, 0f);
        
        private void CreateSettingsOverlay(MinimapUser mapUser)
        {
	        if (!mapUser.Player)
		        return;
	        
	        BaseContainer root = ImageContainer.Create(UI_MINIMAP_SETTINGS, Layer.Hud, UIAnchor.Center, new Offset(-75f, -65f, 65f, 75f))
		        .WithColor(BorderColor)
		        .WithChildren(parent =>
		        {
			        ImageContainer.Create(parent, UIAnchor.TopStretch, new Offset(0f, 0f, 0f, 22f))
				        .WithColor(BorderColor)
				        .WithChildren(header =>
				        {
					        ImageContainer.Create(header, UIAnchor.FullStretch, new Offset(2f, 0f, -2f, -2f))
						        .WithColor(ForegroundColor)
						        .WithChildren(inset =>
						        {
							        TextContainer.Create(inset, UIAnchor.FullStretch, new Offset(2f, 2f, -2f, -2f))
								        .WithSize(12)
								        .WithColor(TextColor)
								        .WithText(GetString("UI.Settings.Title", mapUser.Player))
								        .WithAlignment(TextAnchor.MiddleLeft);
						        });
				        });

			        
			        ImageContainer.Create(parent, UIAnchor.TopRight, new Offset(-24f, -2f, 0f, 22f))
				        .WithColor(BorderColor)
				        .WithChildren(header =>
				        {
					        ImageContainer.Create(header, UIAnchor.BottomRight, new Offset(-22f, 2f, -2f, 22f))
						        .WithColor(ForegroundColor)
						        .WithChildren(close =>
						        {
							        ImageContainer.Create(close, UIAnchor.FullStretch, new Offset(4, 4, -4, -4))
								        .WithSprite(Icon.Icons_Close)
								        .WithColor(TextColor);

							        ButtonContainer.Create(close, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Chaos.UIFramework.Color.Clear)
								        .WithCallback(CallbackHandler, 
									        arg => ChaosUI.Destroy(mapUser.Player, UI_MINIMAP_SETTINGS), 
									        $"{mapUser.Player.UserIDString}.settings.close");
						        });
				        });
			        
			        ImageContainer.Create(parent, UIAnchor.TopRight, new Offset(-46f, -2f, -22f, 22f))
				        .WithColor(BorderColor)
				        .WithChildren(header =>
				        {
					        ImageContainer.Create(header, UIAnchor.BottomRight, new Offset(-22f, 2f, -2f, 22f))
						        .WithColor(ForegroundColor)
						        .WithChildren(close =>
						        {
							        ImageContainer.Create(close, UIAnchor.FullStretch, new Offset(4, 4, -4, -4))
								        .WithSprite(Icon.Icons_Rotate)
								        .WithColor(TextColor);

							        ButtonContainer.Create(close, UIAnchor.FullStretch, Offset.zero)
								        .WithColor(Chaos.UIFramework.Color.Clear)
								        .WithCallback(CallbackHandler, arg =>
									        {
										        if (mapUser.SetPosition(Configuration.UI.Position) || mapUser.SetSize(Configuration.UI.Size))
													UpdateSizeAndOffset(mapUser);
									        }, 
									        $"{mapUser.Player.UserIDString}.settings.reset");
						        });
				        });
			        
			        ImageContainer.Create(parent, UIAnchor.TopCenter, new Offset(-10f, -22f, 10f, -2f))
				        .WithColor(ForegroundColor)
				        .WithChildren(up =>
				        {
					        TextContainer.Create(up, UIAnchor.FullStretch, Offset.zero)
						        .WithText("▲")
						        .WithColor(TextColor)
						        .WithAlignment(TextAnchor.MiddleCenter);
					        
					        ButtonContainer.Create(up, UIAnchor.FullStretch, Offset.zero)
						        .WithColor(Chaos.UIFramework.Color.Clear)
						        .WithCallback(CallbackHandler, arg =>
							        {
								        if (mapUser.SetPosition(mapUser.Position + VerticalMovement))
									        UpdateSizeAndOffset(mapUser);
							        }, $"{mapUser.Player.UserIDString}.move.up");
				        });
			        
			        ImageContainer.Create(parent, UIAnchor.BottomCenter, new Offset(-10f, 2f, 10f, 22f))
				        .WithColor(ForegroundColor)
				        .WithChildren(down =>
				        {
					        TextContainer.Create(down, UIAnchor.FullStretch, Offset.zero)
						        .WithText("▼")
						        .WithColor(TextColor)
						        .WithAlignment(TextAnchor.MiddleCenter);
					        ButtonContainer.Create(down, UIAnchor.FullStretch, Offset.zero)
						        .WithColor(Chaos.UIFramework.Color.Clear)
						        .WithCallback(CallbackHandler, arg =>
							        {
								        if (mapUser.SetPosition(mapUser.Position - VerticalMovement))
									        UpdateSizeAndOffset(mapUser);
							        }, $"{mapUser.Player.UserIDString}.move.down");
				        });
			        
			        ImageContainer.Create(parent, UIAnchor.CenterLeft, new Offset(2f, -10f, 22f, 10f))
				        .WithColor(ForegroundColor)
				        .WithChildren(left =>
				        {
					        TextContainer.Create(left, UIAnchor.FullStretch, Offset.zero)
						        .WithText("◄")
						        .WithColor(TextColor)
						        .WithAlignment(TextAnchor.MiddleCenter);
					        ButtonContainer.Create(left, UIAnchor.FullStretch, Offset.zero)
						        .WithColor(Chaos.UIFramework.Color.Clear)
						        .WithCallback(CallbackHandler, arg =>
							        {
								        if (mapUser.SetPosition(mapUser.Position - HorizontalMovement))
									        UpdateSizeAndOffset(mapUser);
							        }, $"{mapUser.Player.UserIDString}.move.left");
				        });
			        
			        ImageContainer.Create(parent, UIAnchor.CenterRight, new Offset(-22f, -10f, -2f, 10f))
				        .WithColor(ForegroundColor)
				        .WithChildren(right =>
				        {
					        TextContainer.Create(right, UIAnchor.FullStretch, Offset.zero)
						        .WithText("►")
						        .WithColor(TextColor)
						        .WithAlignment(TextAnchor.MiddleCenter);
					        ButtonContainer.Create(right, UIAnchor.FullStretch, Offset.zero)
						        .WithColor(Chaos.UIFramework.Color.Clear)
						        .WithCallback(CallbackHandler, arg =>
							        {
								        if (mapUser.SetPosition(mapUser.Position + HorizontalMovement))
									        UpdateSizeAndOffset(mapUser);
							        }, $"{mapUser.Player.UserIDString}.move.right");
				        });
			        
			        float halfSize = mapUser.Size * 0.5f;
			        
			        ImageContainer.Create(parent, UIAnchor.BottomLeft, new Offset(2f, 2f, 22f, 22f))
				        .WithColor(ForegroundColor)
				        .WithChildren(down =>
				        {
					        TextContainer.Create(down, UIAnchor.FullStretch, Offset.zero)
						        .WithText("\u2199")
						        .WithColor(TextColor)
						        .WithAlignment(TextAnchor.MiddleCenter);
					        ButtonContainer.Create(down, UIAnchor.FullStretch, Offset.zero)
						        .WithColor(Chaos.UIFramework.Color.Clear)
						        .WithCallback(CallbackHandler, arg =>
							        {
								        if (mapUser.SetPosition(new float2(-(640 - halfSize), -(360 - halfSize))))
									        UpdateSizeAndOffset(mapUser);
							        }, $"{mapUser.Player.UserIDString}.move.bottomleft");
				        });
			        
			        ImageContainer.Create(parent, UIAnchor.BottomRight, new Offset(-22f, 2f, -2f, 22f))
				        .WithColor(ForegroundColor)
				        .WithChildren(down =>
				        {
					        TextContainer.Create(down, UIAnchor.FullStretch, Offset.zero)
						        .WithText("\u2198")
						        .WithColor(TextColor)
						        .WithAlignment(TextAnchor.MiddleCenter);
					        ButtonContainer.Create(down, UIAnchor.FullStretch, Offset.zero)
						        .WithColor(Chaos.UIFramework.Color.Clear)
						        .WithCallback(CallbackHandler, arg =>
							        {
								        if (mapUser.SetPosition(new float2(640 - halfSize, -(360 - halfSize))))
									        UpdateSizeAndOffset(mapUser);
							        }, $"{mapUser.Player.UserIDString}.move.bottomright");
				        });
			        
			        ImageContainer.Create(parent, UIAnchor.TopLeft, new Offset(2f, -22f, 22f, -2f))
				        .WithColor(ForegroundColor)
				        .WithChildren(down =>
				        {
					        TextContainer.Create(down, UIAnchor.FullStretch, Offset.zero)
						        .WithText("\u2196")
						        .WithColor(TextColor)
						        .WithAlignment(TextAnchor.MiddleCenter);
					        ButtonContainer.Create(down, UIAnchor.FullStretch, Offset.zero)
						        .WithColor(Chaos.UIFramework.Color.Clear)
						        .WithCallback(CallbackHandler, arg =>
							        {
								        if (mapUser.SetPosition(new float2(-(640 - halfSize), (360 - halfSize))))
									        UpdateSizeAndOffset(mapUser);
							        }, $"{mapUser.Player.UserIDString}.move.topleft");
				        });
			        
			        ImageContainer.Create(parent, UIAnchor.TopRight, new Offset(-22f, -22f, -2f, -2f))
				        .WithColor(ForegroundColor)
				        .WithChildren(down =>
				        {
					        TextContainer.Create(down, UIAnchor.FullStretch, Offset.zero)
						        .WithText("\u2197")
						        .WithColor(TextColor)
						        .WithAlignment(TextAnchor.MiddleCenter);
					        ButtonContainer.Create(down, UIAnchor.FullStretch, Offset.zero)
						        .WithColor(Chaos.UIFramework.Color.Clear)
						        .WithCallback(CallbackHandler, arg =>
							        {
								        if (mapUser.SetPosition(new float2((640 - halfSize), (360 - halfSize))))
									        UpdateSizeAndOffset(mapUser);
							        }, $"{mapUser.Player.UserIDString}.move.topright");
				        });
			        
			        ImageContainer.Create(parent, UIAnchor.Center, new Offset(1f, -10f, 21f, 10f))
				        .WithColor(ForegroundColor)
				        .WithChildren(scaleIn =>
				        {
					        ImageContainer.Create(scaleIn, UIAnchor.FullStretch, new Offset(4, 4, -4, -4))
						        .WithSprite(Icon.Icons_Add)
						        .WithColor(TextColor);

					        ButtonContainer.Create(scaleIn, UIAnchor.FullStretch, Offset.zero)
						        .WithColor(Chaos.UIFramework.Color.Clear)
						        .WithCallback(CallbackHandler, arg =>
							        {
								        if (mapUser.SetSize(mapUser.Size + 5f))
									        UpdateSizeAndOffset(mapUser);
							        }, $"{mapUser.Player.UserIDString}.scale.in");
				        });
			        
			        ImageContainer.Create(parent, UIAnchor.Center, new Offset(-21f, -10f, -1f, 10f))
				        .WithColor(ForegroundColor)
				        .WithChildren(scaleOut =>
				        {
					        ImageContainer.Create(scaleOut, UIAnchor.FullStretch, new Offset(4, 4, -4, -4))
						        .WithSprite(Icon.Icons_Subtract)
						        .WithColor(TextColor);

					        ButtonContainer.Create(scaleOut, UIAnchor.FullStretch, Offset.zero)
						        .WithColor(Chaos.UIFramework.Color.Clear)
						        .WithCallback(CallbackHandler, arg => {
								        if (mapUser.SetSize(mapUser.Size - 5f))
									        UpdateSizeAndOffset(mapUser);
							        }, $"{mapUser.Player.UserIDString}.scale.out");
				        });
		        })
		        .DestroyExisting()
		        .NeedsCursor();

	        ChaosUI.Show(mapUser.Player, root);
        }

        private void UpdateSizeAndOffset(MinimapUser mapUser)
        {
	        if (!mapUser.Player)
		        return;
	        
	        (UIAnchor anchor, Offset containerOffset, float screenSize) = mapUser.GetAnchorOffsetAndSize();
	        
	        UpdateComponent<RectTransformComponent> update = ChaosUI.PrepareUpdate<RectTransformComponent>(UI_MINIMAP);
	        update.Component.Set(anchor, containerOffset);
	        
	        update.MarkFieldsDirty(
		        nameof(RectTransformComponent.AnchorMin), 
			        nameof(RectTransformComponent.AnchorMax), 
			        nameof(RectTransformComponent.OffsetMin), 
			        nameof(RectTransformComponent.OffsetMax));
	        
	        update.Send(mapUser.Player);
        }
        
        private void OnZoomChanged(MinimapUser mapUser, int direction)
        {
	        if (!mapUser.Player)
		        return;
            
	        mapUser.ZoomLevel = Mathf.Clamp(mapUser.ZoomLevel + direction, 0, Configuration.Map.ZoomLevels);
            
	        Vector3 position = mapUser.ConvertPositionToDeepSea(mapUser.LastPosition);
	        Offset mapOffset = _mapUtility.CalculateImageOffsetForPosition(Configuration.UI.Size, position, mapUser.ZoomLevel, Configuration.Map.ZoomLevels);
            
	        UpdateComponent<ScrollViewComponent> update = ChaosUI.PrepareUpdate<ScrollViewComponent>(UI_SCROLLVIEW);
	        update.Component.CopyFrom(_scrollView.WithContentTransform(UIAnchor.Center, mapOffset));

	        UpdateComponent<RectTransformComponent> playerUpdate = ChaosUI.PrepareUpdate<RectTransformComponent>(UI_PLAYER_POSITION_MARKER);
	        
	        float2 markerPosition = _mapUtility.WorldToImage(mapOffset, position);
	        float2 halfSize = new float2(mapOffset.Width * 0.5f, mapOffset.Height * 0.5f);
	        markerPosition = math.clamp(markerPosition, -halfSize, halfSize);

	        playerUpdate.Component.Set(UIAnchor.Center, new Offset(markerPosition.x - 7, markerPosition.y - 7, markerPosition.x + 7, markerPosition.y + 7));
            
	        update.Send(mapUser.Player);
	        playerUpdate.Send(mapUser.Player);
        }

        private void UpdateMapRender(MinimapUser mapUser, Vector3? position, float? rotation, MapLayer? mapLayer)
        {
	        _updates.Clear();
	        
	        if (position.HasValue || mapUser.ForceUpdate)
	        {
		        Vector3 value = mapUser.ConvertPositionToDeepSea(position ?? mapUser.LastPosition);

		        Offset mapOffset = _mapUtility.CalculateImageOffsetForPosition(Configuration.UI.Size, value, mapUser.ZoomLevel, Configuration.Map.ZoomLevels);
		        
		        UpdateComponent<ScrollViewComponent> mapPosition = ChaosUI.PrepareUpdate<ScrollViewComponent>(UI_SCROLLVIEW);
		        mapPosition.Component.CopyFrom(_scrollView.WithContentTransform(UIAnchor.Center, mapOffset));
		        
		        UpdateComponent<RectTransformComponent> playerPosition = ChaosUI.PrepareUpdate<RectTransformComponent>(UI_PLAYER_POSITION_MARKER);
		        float2 markerPosition = _mapUtility.WorldToImage(mapOffset, value);
		        float2 halfSize = new float2(mapOffset.Width * 0.5f, mapOffset.Height * 0.5f);
		        markerPosition = math.clamp(markerPosition, -halfSize, halfSize);
		        playerPosition.Component.Set(UIAnchor.Center, new Offset(markerPosition.x - 7, markerPosition.y - 7, markerPosition.x + 7, markerPosition.y + 7));//8471
		        playerPosition.MarkFieldsDirty(nameof(RectTransformComponent.OffsetMin), nameof(RectTransformComponent.OffsetMax));
		        
		        _updates.Add(mapPosition);
		        _updates.Add(playerPosition);
	        }
	        
	        if (rotation.HasValue || mapUser.ForceUpdate)
	        {
		        float value = rotation ?? mapUser.LastRotation;
		        
		        UpdateComponent<RawImageComponent> mapArrow = ChaosUI.PrepareUpdate<RawImageComponent>(UI_PLAYER_ROTATION_MARKER);
		        mapArrow.Component.PNG = GetClosestDirectionIcon(value);
		        mapArrow.MarkFieldsDirty(nameof(RawImageComponent.PNG));
		        _updates.Add(mapArrow);
	        }
	        
	        if (mapLayer.HasValue || mapUser.ForceUpdate)
	        {
		        MapLayer value = mapLayer ?? mapUser.LastLayer;
		        
		        UpdateComponent<RawImageComponent> mapImage = ChaosUI.PrepareUpdate<RawImageComponent>(UI_MAP_IMAGE);
		        mapImage.Component.PNG = GetMapForCurrentLayer(value);
		        mapImage.MarkFieldsDirty(nameof(RawImageComponent.PNG));
		        _updates.Add(mapImage);

		        if (Configuration.Map.RenderMonumentNames || Configuration.Map.RenderTunnelEntrances || Configuration.Map.DeepSea.RenderDeepSeaMarkers)
		        {
			        string markers = GetMarkersForCurrentLayer(value);
			        
			        UpdateComponent<RawImageComponent> markerImage = ChaosUI.PrepareUpdate<RawImageComponent>(UI_MARKER_OVERLAY);
			        
			        if (!string.IsNullOrEmpty(markers))
			        {
				        markerImage.Component.PNG = markers;
				        markerImage.Component.Color = Chaos.UIFramework.Color.White;
				        markerImage.MarkFieldsDirty(nameof(RawImageComponent.PNG));
				        markerImage.MarkFieldsDirty(nameof(RawImageComponent.Color));
			        }
			        else
			        {
				        markerImage.Component.Color = Chaos.UIFramework.Color.Clear;
				        markerImage.MarkFieldsDirty(nameof(RawImageComponent.Color));
			        }
			        
			        _updates.Add(markerImage);
		        }
	        }

	        if (_updates.Count > 0)
				ChaosUI.SendUpdates(mapUser.Player, _updates);
	        
	        mapUser.ForceUpdate = false;
        }

        private string GetMapForCurrentLayer(MapLayer mapLayer) 
	        => !_mapLayers.TryGetValue(mapLayer, out string map) ? _mapLayers[MapLayer.Overworld] : map;

        private string GetMarkersForCurrentLayer(MapLayer mapLayer)
        {
	        if (mapLayer is >= MapLayer.TrainTunnels and <= MapLayer.Dungeons)
		        return _mapLayers[MapLayer.UnderworldMarkers];
	        
	        if (mapLayer == MapLayer.DeepSea)
		        return _mapLayers[MapLayer.DeepSeaMarkers];

	        return _mapLayers[MapLayer.OverworldMarkers];
        }

        #endregion
        
        #region Commands

        public void cmdMinimap(BasePlayer player, string command, string[] args)
        {
	        if (!player.HasPermission(USE_PERMISSION))
	        {
		        player.LocalizedMessage(this, "Error.NoPermission");
		        return;
	        }

	        if (!_isPluginReady)
	        {
		        player.LocalizedMessage(this, "Error.Rendering");
		        return;
	        }

	        _userData.Data.TryGetValue(player.userID.Get(), out MinimapUser mapUser);

	        if (mapUser != null && args is { Length: 1 } && args[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
	        {
		        if (mapUser.SetSize(Configuration.UI.Size) || mapUser.SetPosition(Configuration.UI.Position))
		        {
			        if (mapUser.IsActive)
				        UpdateSizeAndOffset(mapUser);
		        }

		        return;
	        }

	        if (mapUser is not { IsActive: true })
		        MapManager.Register(player);
	        else
	        {
		        mapUser.IsActive = false;
		        ChaosUI.Destroy(player, UI_MINIMAP);
	        }
        }
        
        
        public void ccmdRegenerate(ConsoleSystem.Arg arg)
        {
	        BasePlayer player = arg.Player();
	        if (player && !player.IsAdmin)
	        {
		        player.LocalizedMessage(this, "Error.NoPermission");
		        return;
	        }
	        
	        SendReply(arg, "Regenerating map images...");
	        MapManager.Destroy();
	        
	        _isPluginReady = false;
	        _pendingImages = 0;
	        _imagesStored = 0;
	        
	        MapManager.Initialize(InitialMapRender, UpdateMapRender);
	        
	        ImportMapArrows();
	        ServerMgr.Instance.StartCoroutine(RegenerateMaps());
        }
        
        public void ccmdMinimapReset(ConsoleSystem.Arg arg)
        {
	        BasePlayer player = arg.Player();
	        if (!player)
		        return;

	        if (!player.HasPermission(USE_PERMISSION))
	        {
		        player.LocalizedMessage(this, "Error.NoPermission");
		        return;
	        }
	        
	        if (!_isPluginReady)
	        {
		        player.LocalizedMessage(this, "Error.Rendering");
		        return;
	        }

	        if (!_userData.Data.TryGetValue(player.userID.Get(), out MinimapUser mapUser))
		        return;
	        
	        if (mapUser.SetSize(Configuration.UI.Size) || mapUser.SetPosition(Configuration.UI.Position))
	        {
		        if (mapUser.IsActive)
			        UpdateSizeAndOffset(mapUser);
	        }
        }

        public void ccmdMinimapToggle(ConsoleSystem.Arg arg)
        {
	        BasePlayer player = arg.Player();
	        if (!player)
		        return;

	        if (!player.HasPermission(USE_PERMISSION))
	        {
		        player.LocalizedMessage(this, "Error.NoPermission");
		        return;
	        }
	        
	        if (!_isPluginReady)
	        {
		        player.LocalizedMessage(this, "Error.Rendering");
		        return;
	        }

	        _userData.Data.TryGetValue(player.userID.Get(), out MinimapUser mapUser);

	        if (mapUser is not { IsActive: true })
		        MapManager.Register(player);
	        else
	        {
		        mapUser.IsActive = false;
		        ChaosUI.Destroy(player, UI_MINIMAP);
	        }
        }
        
        public void ccmdMinimapZoomIn(ConsoleSystem.Arg arg)
        {
	        BasePlayer player = arg.Player();
	        if (!player)
		        return;

	        if (!player.HasPermission(USE_PERMISSION))
	        {
		        player.LocalizedMessage(this, "Error.NoPermission");
		        return;
	        }
	        
	        if (!_userData.Data.TryGetValue(player.userID.Get(), out MinimapUser mapUser))
		        return;
	        
	        mapUser.ZoomLevel = Mathf.Clamp(mapUser.ZoomLevel + 1, 0, Configuration.Map.ZoomLevels);
	        mapUser.ForceUpdate = true;
        }
        
        public void ccmdMinimapZoomOut(ConsoleSystem.Arg arg)
        {
	        BasePlayer player = arg.Player();
	        if (!player)
		        return;

	        if (!player.HasPermission(USE_PERMISSION))
	        {
		        player.LocalizedMessage(this, "Error.NoPermission");
		        return;
	        }
	        
	        if (!_userData.Data.TryGetValue(player.userID.Get(), out MinimapUser mapUser))
		        return;
	        
	        mapUser.ZoomLevel = Mathf.Clamp(mapUser.ZoomLevel - 1, 0, Configuration.Map.ZoomLevels);
	        mapUser.ForceUpdate = true;
        }

        public void ccmdMinimapRender(ConsoleSystem.Arg arg)
        {
	        BasePlayer player = arg.Player();
	        if (player && !player.IsAdmin)
		        return;
	        
	        SendReply(arg, "[Minimap] Re-rendering world map layers");
	        ImportMapArrows();
	        ServerMgr.Instance.StartCoroutine(RegenerateMaps());
        }
        
        /*[ConsoleCommand("minimap.render.mainland")]
        private void ccmdMinimapRenderMainland(ConsoleSystem.Arg arg)
        {
	        BasePlayer player = arg.Player();
	        if (player && !player.IsAdmin)
		        return;
	        
	        SendReply(arg, "[Minimap] Re-rendering mainland map layers");
	        ServerMgr.Instance.StartCoroutine(TimedCoroutine.Run(GenerateMaps(), time =>
	        {
		        SendReply(arg, $"[Minimap] Mainland map generation completed in {time:F2}ms");
	        }));
        }
        
        [ConsoleCommand("minimap.render.deepsea")]
        private void ccmdMinimapRenderDeepSea(ConsoleSystem.Arg arg)
        {
	        BasePlayer player = arg.Player();
	        if (player && !player.IsAdmin)
		        return;

	        if (!DeepSeaManager.ServerInstance.IsOpen())
	        {
		        SendReply(arg, "[Minimap] Deep sea is not open");
		        return;
	        }
	        
	        SendReply(arg, "[Minimap] Re-rendering deep sea map layers");
	        ServerMgr.Instance.StartCoroutine(TimedCoroutine.Run(GenerateDeepSeaMap(), time =>
	        {
		        SendReply(arg, $"[Minimap] Deep sea map regeneration completed in {time:F2}ms");
	        }));
        }*/
        
        #endregion
        
        #region Localization
        
        private Dictionary<string, string> Messages => new()
        {
	        ["Error.NoPermission"] = "You do not have permission to use this command",
	        ["Error.Rendering"] = "The minimap is currently rendering, please try again soon",
	        ["UI.Settings.Title"] = "Minimap Editor"
        };

        private void RegisterMessages()
        {
	        MinimapHost.Instance?.Lang?.RegisterMessages(Messages);
        }

        private string GetString(string key, BasePlayer player) =>
	        MinimapHost.Instance?.Lang?.GetMessage(key, player?.UserIDString) ?? key ?? "";

        private void SendReply(ConsoleSystem.Arg arg, string message)
        {
	        if (arg == null)
	        {
		        Debug.Log("[Minimap] " + message);
		        return;
	        }
	        BasePlayer player = arg.Player();
	        if (player != null)
		        player.ChatMessage(message);
	        else
		        arg.ReplyWith(message);
        }

        #endregion

        #region Configuration

        internal static ConfigData Configuration;

        private void LoadConfiguration()
        {
	        string path = MinimapHost.Instance?.ConfigPath;
	        string oxideFallback = Path.Combine(MinimapHost.Instance?.ServerRoot ?? ".", "oxide", "config", "Minimap.json");

	        try
	        {
		        if (!string.IsNullOrEmpty(path) && File.Exists(path))
		        {
			        Configuration = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(path));
		        }
		        else if (File.Exists(oxideFallback))
		        {
			        Configuration = JsonConvert.DeserializeObject<ConfigData>(File.ReadAllText(oxideFallback));
			        Debug.Log("[Minimap] Migrated config from oxide/config/Minimap.json");
		        }
	        }
	        catch (Exception ex)
	        {
		        Debug.LogWarning("[Minimap] LoadConfig: " + ex.Message);
	        }

	        if (Configuration == null)
		        Configuration = GenerateDefaultConfiguration();

	        Configuration.Map ??= new ConfigData.MapSettings();
	        Configuration.Map.DeepSea ??= new DeepSeaMapConfig();
	        Configuration.UI ??= new ConfigData.UISettings();
	        Configuration.Heat ??= new ConfigData.HeatSettings();
	        if (Configuration.Heat.HeatColors == null || Configuration.Heat.HeatColors.Length == 0)
		        Configuration.Heat.HeatColors = ConfigData.HeatSettings.DefaultColors();
	        if (Configuration.UI.Position == null)
		        Configuration.UI.Position = new ConfigData.UISettings.ScreenPosition(531, 251);

	        SaveConfiguration();
        }

        private void SaveConfiguration()
        {
	        string path = MinimapHost.Instance?.ConfigPath;
	        if (string.IsNullOrEmpty(path) || Configuration == null) return;
	        try
	        {
		        var dir = Path.GetDirectoryName(path);
		        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			        Directory.CreateDirectory(dir);
		        File.WriteAllText(path, JsonConvert.SerializeObject(Configuration, Formatting.Indented));
	        }
	        catch (Exception ex)
	        {
		        Debug.LogWarning("[Minimap] SaveConfig: " + ex.Message);
	        }
        }
        
        internal class ConfigData
        {
	        [JsonProperty("Map Settings")]
	        public MapSettings Map = new MapSettings();
	        
	        [JsonProperty("UI Settings")]
	        public UISettings UI = new UISettings();

	        [JsonProperty("Heat Map Settings")]
	        public HeatSettings Heat = new HeatSettings();

	        [JsonProperty("Version")]
	        public VersionNumber Version { get; set; }

	        public class MapSettings : MapConfig
	        {
		        [JsonProperty("Zoom levels")]
		        public int ZoomLevels { get; set; }
		        
		        [JsonProperty("Default zoom level")]
		        public int DefaultZoomLevel { get; set; }
		        
		        [JsonProperty("Enable train tunnel map")]
		        public bool EnableTunnels { get; set; }
		        
		        [JsonProperty("Enable underwater labs map")]
		        public bool EnableLabs { get; set; }
		        
		        [JsonProperty("Enabled by default")]
		        public bool EnabledByDefault { get; set; }
		        
		        [JsonProperty("Deep Sea Settings")]
		        public DeepSeaMapConfig DeepSea = new DeepSeaMapConfig();
	        }
	        
	        public class UISettings
	        {
		        [JsonProperty("Screen position (base screen size is 1280x720)")]
		        public ScreenPosition Position { get; set; }
		        
		        [JsonProperty("Minimap screen size (pixels)")]
		        public float Size { get; set; }
		        
		        [JsonProperty("Border color")]
		        public HexColor.Rgba BorderColor { get; set; }
		        
		        [JsonProperty("Foreground color")]
		        public HexColor.Rgba ForegroundColor { get; set; }
		        
		        [JsonProperty("Text color")]
		        public HexColor.Rgba TextColor { get; set; }
		        
		        [JsonProperty("Marker color")]
		        public HexColor.Rgba MarkerColor { get; set; }

		        public class ScreenPosition
		        {
			        [JsonProperty("Horizontal (-640.0 -> 640.0)")]
			        public float X { get; set; } = 531f;

			        [JsonProperty("Vertical (-360.0 -> 360.0)")]
			        public float Y { get; set; } = 251f;
			        
			        public ScreenPosition(){}
			        
			        public ScreenPosition(float x, float y)
			        {
				        X = x;
				        Y = y;
			        }
			        
			        public static implicit operator float2(ScreenPosition position) => new(position.X, position.Y);
		        }
	        }

	        public class HeatSettings
	        {
		        [JsonProperty("Enable PVP heat (player vs player deaths)")]
		        public bool EnablePvp { get; set; }

		        [JsonProperty("Enable PVE heat (NPC locations)")]
		        public bool EnablePve { get; set; }

		        [JsonProperty("Store PVP heat data between server starts")]
		        public bool StoreData { get; set; } = true;

		        [JsonProperty("Heat grid size in world units")]
		        public float GridSize { get; set; } = 50f;

		        [JsonProperty("The rate in which the heat overlay is updated in seconds")]
		        public float UpdateRate { get; set; } = 60f;

		        [JsonProperty("How many events in a cell for it to register as hottest value")]
		        public float MaxCellValue { get; set; } = 5f;

		        [JsonProperty("The maximum opacity of the cell drawn on the map (0.0 - 1.0)")]
		        public float MaxCellOpacity { get; set; } = 0.75f;

		        [JsonProperty("Fade opacity of a heat event using the time remaining until it expires")]
		        public bool FadeOpacity { get; set; } = true;

		        [JsonProperty("The value of a heat event (0.0 - 1.0)")]
		        public float HeatValue { get; set; } = 1f;

		        [JsonProperty("The time in seconds before a PVP heat event expires")]
		        public float ExpireTime { get; set; } = 1800f;

		        [JsonProperty("Heat colors in order of lowest to highest")]
		        public HexColor.Rgba[] HeatColors { get; set; } = DefaultColors();

		        public static HexColor.Rgba[] DefaultColors()
		        {
			        return new[]
			        {
				        new HexColor.Rgba("000000", 0f),
				        new HexColor.Rgba("7600C5", 0.1960784f),
				        new HexColor.Rgba("002DFF", 0.3921569f),
				        new HexColor.Rgba("00FAFF", 0.4901961f),
				        new HexColor.Rgba("00FF1E", 0.5882353f),
				        new HexColor.Rgba("FF8400", 0.7058824f),
				        new HexColor.Rgba("FF0003", 0.7058824f)
			        };
		        }
	        }
        }

        private ConfigData GenerateDefaultConfiguration()
        {
            return new ConfigData
            {
	            Map = new ConfigData.MapSettings
	            {
		            ZoomLevels = 10,
		            DefaultZoomLevel = 5,
		            EnableLabs = true,
		            EnableTunnels = true,
		            Underworld = new MapConfig.UnderworldColors(),
		            DeepSea = new DeepSeaMapConfig()
	            },
	            UI = new ConfigData.UISettings
	            {
		            BorderColor = new HexColor.Rgba("1C1A16"),
		            ForegroundColor = new HexColor.Rgba("5D7239"),
		            TextColor = new HexColor.Rgba("B0CC80"),
		            MarkerColor = new HexColor.Rgba("FFD272"),
		            Size = 200,
		            Position = new ConfigData.UISettings.ScreenPosition(531, 251)
	            },
	            Heat = new ConfigData.HeatSettings(),
	            Version = MinimapHarmonyMod.Version
            };
        }

        #endregion
        
        #region Data
        public class MinimapUser : BaseMapUser
        {
	        public int ZoomLevel = 1;

	        [JsonConverter(typeof(float2Converter))]
	        public float2 Position = new float2(0, 0);
	        
	        public float Size = 0f;

	        [JsonIgnore]
	        public bool MouseHelperOpen;

	        [JsonIgnore]
	        public bool ForceUpdate;

	        [JsonIgnore]
	        public Vector3 LastPosition = Vector3.zero;
	            
	        [JsonIgnore]
	        public float LastRotation;
	        
	        [JsonIgnore]
	        public MapLayer LastLayer = MapLayer.Overworld;

	        [JsonIgnore]
	        private EnvironmentType _lastEnvironment = EnvironmentType.Outdoor;
	        
	        [JsonIgnore]
	        private int _lastFloor = 0;

	        [JsonIgnore]
	        private float _nextEnvironmentUpdate;
	        
	        [JsonIgnore]
	        private static List<EnvironmentVolume> _environmentVolumes = new();

	        public static event Action<BaseMapUser> OnEnterExitDeepSea;
	        
	        public MinimapUser(){}
	        
	        public MinimapUser(int zoomLevel)
	        {
		        Size = Configuration.UI.Size;
		        Position = RectTransformUtils.ClampPosition(Configuration.UI.Position, Size);
		        
		        ZoomLevel = zoomLevel;
	        }

	        public (UIAnchor anchor, Offset offset, float size) GetAnchorOffsetAndSize()
	        {
		        if (Size == 0f)
			        Size = Configuration.UI.Size;
		        
		        if (Position is { x: 0, y: 0 })
			        Position = Configuration.UI.Position;

		        Position = RectTransformUtils.ClampPosition(Position, Size);

		        UIAnchor.Enum @enum = RectTransformUtils.PositionToAnchorType(Position);
		        Offset offset = RectTransformUtils.CalculateOffsetToAnchor(@enum, Position, Size);
		        UIAnchor anchor = RectTransformUtils.EnumToAnchor(@enum);

		        return (anchor, offset, Size);
	        }

	        public bool SetPosition(float2 position)
	        {
		        position = RectTransformUtils.ClampPosition(position, Size);
		        
		        if (position.Equals(Position))
			        return false;
		        
		        Position = position;
		        return true;
	        }
	        
	        public bool SetSize(float size)
	        {
		        size = Mathf.Clamp(size, 100, 400);
		        if (size == Size)
			        return false;
		        
		        Size = size;
		        return true;
	        }

	        public Vector3 ConvertPositionToDeepSea(Vector3 position)
	        {
		        if (DeepSeaManager.IsInsideDeepSea(Player))
		        {
			        Vector3 normalizedCenter = DeepSeaManager.NormalizePosInDeepSea(Player.transform.position);
			        return TerrainMeta.Denormalize(normalizedCenter);
		        }

		        return position;
	        }

	        public void GetInitial(out Vector3 position, out float rotation, out MapLayer mapLayer)
	        {
		        LastPosition = position = Player.transform.position;
		        LastRotation = rotation = Player.eyes.rotation.eulerAngles.y;
		        
		        (EnvironmentType currentEnvironment, int floor) = EnvironmentUpdate(LastPosition);
		        LastLayer = mapLayer = GetMapLayerFromEnvironment(currentEnvironment, floor);
		        _lastFloor = floor;
	        }
	        
	        public bool ShouldUpdate(out Vector3? position, out float? rotation, out MapLayer? mapLayer)
	        {
		        Vector3 currentPosition = Player.transform.position;
		        float currentRotation = Player.eyes.rotation.eulerAngles.y;
		        
		        (EnvironmentType currentEnvironment, int floor) = EnvironmentUpdate(currentPosition);
		        
		        MapLayer currentLayer = GetMapLayerFromEnvironment(currentEnvironment, floor);
		        
		        bool result = false;
		        position = null;
		        rotation = null;
		        mapLayer = null;
		        
		        if (LastPosition != currentPosition)
		        {
			        position = currentPosition;
			        LastPosition = currentPosition;
			        result = true;
		        }
		        
		        if (LastRotation != currentRotation)
		        {
			        rotation = currentRotation;
			        LastRotation = currentRotation;
			        result = true;
		        }
		        
		        if (LastLayer != currentLayer)
		        {
			        if (LastLayer != MapLayer.DeepSea && currentLayer == MapLayer.DeepSea)
				        OnEnterExitDeepSea?.Invoke(this);
			        
			        if (LastLayer == MapLayer.DeepSea && currentLayer != MapLayer.DeepSea)
				        OnEnterExitDeepSea?.Invoke(this);
			        
			        mapLayer = currentLayer;
			        LastLayer = currentLayer;
			        result = true;
		        }
		        
		        return result;
	        }
	        
	        private (EnvironmentType, int) EnvironmentUpdate(Vector3 position)
	        {
		        if (Time.realtimeSinceStartup < _nextEnvironmentUpdate)
			        return (_lastEnvironment, _lastFloor);
		        
		        (EnvironmentType environmentType, int floor) = GetCurrentEnvironment(position);
		        _nextEnvironmentUpdate = Time.realtimeSinceStartup + 1f;
		        _lastEnvironment = environmentType;
		        _lastFloor = floor;
		        return (environmentType, floor);
	        }

	        private (EnvironmentType, int) GetCurrentEnvironment(Vector3 position)
	        {
		        EnvironmentType mask = GetEnvironmentTypeAndVolumes(position);
		        if ((int)mask == 0)
		        {
			        if (IsInTrainTunnelLayer(position))
				        return (EnvironmentType.TrainTunnels, 0);
			        return (EnvironmentType.Outdoor, 0);
		        }

		        if ((mask & EnvironmentType.TrainTunnels) != 0)
			        return (EnvironmentType.TrainTunnels, 0);

		        if ((mask & EnvironmentType.UnderwaterLab) != 0)
		        {
			        for (int i = 0; i < _environmentVolumes.Count; i++)
			        {
				        EnvironmentVolume environmentVolume = _environmentVolumes[i];
				        if (VolumeLookup.TryGetValue(environmentVolume, out (DungeonBaseLink dungeonBaseLink, int floor) info) && info.dungeonBaseLink)
					        return (EnvironmentType.UnderwaterLab, info.floor);
			        }

			        return (EnvironmentType.UnderwaterLab, 0);
		        }

		        if (IsInTrainTunnelLayer(position))
			        return (EnvironmentType.TrainTunnels, 0);

		        return (EnvironmentType.Outdoor, 0);
	        }

	        private static bool IsInTrainTunnelLayer(Vector3 position)
	        {
		        if (TerrainMeta.HeightMap == null)
			        return false;
		        if (CaveNetworkGroupLayerOverride.Includes(position))
			        return false;
		        float depth = position.y - TerrainMeta.HeightMap.GetHeight(position);
		        return depth < -20f;
	        }
	        
	        private EnvironmentType GetEnvironmentTypeAndVolumes(Vector3 position)
	        {
		        _environmentVolumes.Clear();
		        
		        EnvironmentType environmentType = EnvironmentManager.Get(position, ref _environmentVolumes, 1f);
		        
		        for (int i = 0; i < _environmentVolumes.Count; i++)
			        environmentType |= _environmentVolumes[i].Type;
		        
		        return environmentType;
	        }

	        private MapLayer GetMapLayerFromEnvironment(EnvironmentType environment, int floor)
	        {
		        if (DeepSeaManager.IsInsideDeepSea(Player))
			        return MapLayer.DeepSea;

		        if ((environment & EnvironmentType.TrainTunnels) != 0)
			        return MapLayer.TrainTunnels;

		        if ((environment & EnvironmentType.UnderwaterLab) != 0)
			        return floor + MapLayer.Underwater1;

		        if ((environment & EnvironmentType.Submarine) != 0)
			        return MapLayer.Underwater1;

		        return MapLayer.Overworld;
	        }
        }

        private class RectTransformUtils
        {
	        private const float HALF_WIDTH = 640;
	        private const float HALF_HEIGHT = 360;
	        
	        public static float2 ClampPosition(float2 position, float size)
	        {
		        float halfSize = size * 0.5f;
		        
		        return new float2(
			        Mathf.Clamp(position.x, -HALF_WIDTH + halfSize, HALF_WIDTH - halfSize), 
			        Mathf.Clamp(position.y, -HALF_HEIGHT + halfSize, HALF_HEIGHT - halfSize));
	        }

	        public static UIAnchor.Enum PositionToAnchorType(float2 position)
	        {
		        if (position is { x: < 0, y: 0 })
			        return UIAnchor.Enum.CenterLeft;
			        
		        if (position is { x: > 0, y: 0 })
			        return UIAnchor.Enum.CenterRight;
			        
		        if (position is { x: 0, y: < 0 })
			        return UIAnchor.Enum.BottomCenter;
			        
		        if (position is { x: 0, y: > 0 })
			        return UIAnchor.Enum.TopCenter;
			        
		        if (position is { x: < 0, y: < 0 })
			        return UIAnchor.Enum.BottomLeft;
			        
		        if (position is { x: > 0, y: < 0 })
			        return UIAnchor.Enum.BottomRight;
			        
		        if (position is { x: < 0, y: > 0 })
			        return UIAnchor.Enum.TopLeft;
			        
		        if (position is { x: > 0, y: > 0 })
			        return UIAnchor.Enum.TopRight;
			        
		        return UIAnchor.Enum.Center;
	        }
	        
	        public static UIAnchor EnumToAnchor(UIAnchor.Enum @enum)
	        {
		        return @enum switch
		        {
			        UIAnchor.Enum.TopLeft => UIAnchor.TopLeft,
			        UIAnchor.Enum.TopCenter => UIAnchor.TopCenter,
			        UIAnchor.Enum.TopRight => UIAnchor.TopRight,
			        UIAnchor.Enum.CenterLeft => UIAnchor.CenterLeft,
			        UIAnchor.Enum.CenterRight => UIAnchor.CenterRight,
			        UIAnchor.Enum.BottomLeft => UIAnchor.BottomLeft,
			        UIAnchor.Enum.BottomCenter => UIAnchor.BottomCenter,
			        UIAnchor.Enum.BottomRight => UIAnchor.BottomRight,
			        _ => UIAnchor.Center
		        };
	        }
	        
	        public static float2 GetEffectiveAnchor(UIAnchor.Enum anchorEnum)
	        {
		        return anchorEnum switch
		        {
			        UIAnchor.Enum.TopRight     => new float2( HALF_WIDTH, HALF_HEIGHT),
			        UIAnchor.Enum.TopLeft      => new float2(-HALF_WIDTH, HALF_HEIGHT),
			        UIAnchor.Enum.BottomRight  => new float2( HALF_WIDTH, -HALF_HEIGHT),
			        UIAnchor.Enum.BottomLeft   => new float2(-HALF_WIDTH, -HALF_HEIGHT),
			        UIAnchor.Enum.CenterRight  => new float2( HALF_WIDTH, 0),
			        UIAnchor.Enum.CenterLeft   => new float2(-HALF_WIDTH, 0),
			        UIAnchor.Enum.TopCenter    => new float2(0, HALF_HEIGHT),
			        UIAnchor.Enum.BottomCenter => new float2(0, -HALF_HEIGHT),
			        _                       => new float2(0, 0),
		        };
	        }

	        public static Offset CalculateOffsetToAnchor(UIAnchor.Enum @enum, float2 position, float size)
	        {
		        float halfSize = size * 0.5f;
    
		        float2 effectiveAnchor = GetEffectiveAnchor(@enum);
    
		        float2 anchoredPos = position - effectiveAnchor;
    
		        float2 offMin = anchoredPos - new float2(halfSize, halfSize);
		        float2 offMax = offMin + new float2(size, size);
    
		        return new Offset(offMin.x, offMin.y, offMax.x, offMax.y);
	        }
        }
 
        #endregion       
    }    
}        