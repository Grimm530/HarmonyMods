using System;
using System.Collections.Generic;
using System.IO;
using Oxide.Core;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.Networking;

namespace Oxide.Plugins
{
    public partial class HitMarkers
    {
        internal static HitMarkers GetModInstance() => _instance;
        internal static void SetInstance(HitMarkers inst) => _instance = inst;
        internal static void ClearInstance() => _instance = null;

        public void CallInit()
        {
            try { Init(); }
            catch (Exception ex) { Debug.LogWarning("[HitMarkers] Init: " + ex.Message); }
            try { HarmonyLoadDefaultMessages(); }
            catch (Exception ex) { Debug.LogWarning("[HitMarkers] LoadDefaultMessages: " + ex.Message); }
            try { RegisterHeadshotMessages(); }
            catch (Exception ex) { Debug.LogWarning("[HitMarkers] Headshot messages: " + ex.Message); }
            try { MergeLegacyHeadshotData(); }
            catch { }
        }

        private void MergeLegacyHeadshotData()
        {
            if (_data == null) return;
            if (_data.HeadshotDisabledUsers == null)
                _data.HeadshotDisabledUsers = new List<ulong>();
            try
            {
                var old = Interface.Oxide.DataFileSystem.ReadObject<List<ulong>>("HitIcon");
                // HitIcon.json may be { DisabledUsers: [] }
            }
            catch { }
            try
            {
                var path = System.IO.Path.Combine(Oxide.Core.OxideMod.ResolveServerRoot(), "HarmonyData", "HitIcon.json");
                if (!System.IO.File.Exists(path)) return;
                var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
                var arr = json["DisabledUsers"] as Newtonsoft.Json.Linq.JArray;
                if (arr == null) return;
                foreach (var t in arr)
                {
                    ulong id = t.ToObject<ulong>();
                    if (!_data.HeadshotDisabledUsers.Contains(id))
                        _data.HeadshotDisabledUsers.Add(id);
                }
            }
            catch { }
        }

        public void CallOnServerInitialized()
        {
            try { OnServerInitialized(); }
            catch (Exception ex) { Debug.LogError("[HitMarkers] OnServerInitialized: " + ex); }
            try { LoadHeadshotImages(); }
            catch (Exception ex) { Debug.LogWarning("[HitMarkers] Headshot images: " + ex.Message); }
        }

        public void CallUnload()
        {
            try { Unload(); }
            catch (Exception ex) { Debug.LogWarning("[HitMarkers] Unload: " + ex.Message); }
            try { UnloadHeadshot(); }
            catch { }
        }

        public void OnHurtObserved(BaseCombatEntity entity, HitInfo info, float healthBefore)
        {
            if (entity == null || info == null) return;

            if (entity is BuildingBlock block)
            {
                OnEntityTakeDamage(block, info);
                return;
            }

            var attacker = info.InitiatorPlayer;
            if (attacker == null || attacker.IsNpc || HasPermission(attacker) == false) return;
            if (entity is BaseCorpse || entity is BuildingBlock) return;
            if (!_config.ShowAnimalDamage && entity is BaseAnimalNPC) return;
            if (!_config.ShowNpcDamage && (entity is BaseNpc || (entity is BasePlayer bp && bp.IsNpc))) return;

            var damageDone = healthBefore - entity.Health();
            if (damageDone <= 0f) return;

            GetOrAddMarker(attacker).ShowHit(entity, info, damageDone);

            if (info.isHeadshot && _config.HeadshotIcon != null && _config.HeadshotIcon.ShowOnHeadshot)
                ShowHeadshotIcon(attacker, false);
        }

        public void OnEntityDied(BaseCombatEntity entity, HitInfo info)
        {
            SendHeadshotDeath(entity, info);
        }

        public void OnPlayerDisconnectedHarmony(BasePlayer player)
        {
            if (player == null) return;
            DestroyHeadshotUi(player);
        }

        #region HeadshotIcon (merged from oxide/plugins/HeadshotIcon.cs)

        private readonly Dictionary<string, string> _headshotImages = new Dictionary<string, string>();

        private void RegisterHeadshotMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["HeadshotEnabled"] = "Hit icon was <color=green>enabled</color>",
                ["HeadshotDisabled"] = "Hit icon was <color=red>disabled</color>"
            }, this);
        }

        private void LoadHeadshotImages()
        {
            if (_config?.HeadshotIcon == null) return;
            string root = Oxide.Core.OxideMod.ResolveServerRoot();
            string dir = Path.Combine(root, "HarmonyImages", "HitMarkers");
            TryStorePng("hitimage", Path.Combine(dir, "hit.png"));
            TryStorePng("deathimage", Path.Combine(dir, "death.png"));
        }

        private void TryStorePng(string name, string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                PrintWarning("[HitMarkers] Headshot image missing: " + filePath);
                return;
            }
            if (ServerMgr.Instance == null) return;
            ServerMgr.Instance.StartCoroutine(StorePngFile(name, filePath));
        }

        private System.Collections.IEnumerator StorePngFile(string name, string filePath)
        {
            var url = "file://" + filePath.Replace('\\', '/');
            using (var www = UnityWebRequestTexture.GetTexture(url))
            {
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                {
                    PrintError("[HitMarkers] Failed to load " + filePath + ": " + www.error);
                    yield break;
                }
                var texture = DownloadHandlerTexture.GetContent(www);
                try
                {
                    var bytes = texture.EncodeToPNG();
                    var pngId = FileStorage.server.Store(bytes, FileStorage.Type.png, CommunityEntity.ServerInstance.net.ID);
                    _headshotImages[name] = pngId.ToString();
                    _loadedImages[name] = pngId.ToString();
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        private void UnloadHeadshot()
        {
            foreach (var player in BasePlayer.activePlayerList)
                DestroyHeadshotUi(player);
        }

        private void DestroyHeadshotUi(BasePlayer player)
        {
            if (player == null) return;
            CuiHelper.DestroyUi(player, "hitpng");
            CuiHelper.DestroyUi(player, "hitdmg");
        }

        private void SendHeadshotDeath(BaseCombatEntity entity, HitInfo info)
        {
            var cfg = _config?.HeadshotIcon;
            if (cfg == null || !cfg.ShowDeathSkull || info == null || entity == null) return;

            var initiator = info.Initiator as BasePlayer;
            if (initiator == null) return;
            if (_data?.HeadshotDisabledUsers != null && _data.HeadshotDisabledUsers.Contains(initiator.userID))
                return;

            if (entity is BaseNpc)
            {
                if (!cfg.ShowNpc) return;
                NextTick(() => ShowHeadshotIcon(initiator, true));
                return;
            }

            var victim = entity as BasePlayer;
            if (victim == null || victim == initiator) return;
            NextTick(() => ShowHeadshotIcon(initiator, true));
        }

        private void ShowHeadshotIcon(BasePlayer player, bool isKill)
        {
            if (player == null || player.net?.connection == null) return;
            DestroyHeadshotUi(player);

            string imageKey = isKill ? "deathimage" : "hitimage";
            if (!_headshotImages.TryGetValue(imageKey, out var png) || string.IsNullOrEmpty(png))
                return;

            string color = isKill
                ? (_config.HeadshotIcon.ColorDeath ?? "1 0 0 1")
                : (_config.HeadshotIcon.ColorBody ?? "1 1 1 1");

            var container = new CuiElementContainer();
            container.Add(new CuiElement
            {
                Name = "hitpng",
                Parent = "Hud",
                FadeOut = 0.2f,
                Components =
                {
                    new CuiRawImageComponent
                    {
                        Png = png,
                        Color = color,
                        Sprite = "assets/content/textures/generic/fulltransparent.tga"
                    },
                    new CuiRectTransformComponent
                    {
                        AnchorMin = "0.487 0.482",
                        AnchorMax = "0.513 0.518"
                    }
                }
            });
            CuiHelper.AddUi(player, container);

            float destroy = _config.HeadshotIcon.TimeToDestroy > 0f ? _config.HeadshotIcon.TimeToDestroy : 0.45f;
            timer.Once(destroy, () => DestroyHeadshotUi(player));
        }

        [ChatCommand("hit")]
        private void ToggleHeadshotIcon(BasePlayer player, string command, string[] args)
        {
            if (player == null || _data == null) return;
            if (_data.HeadshotDisabledUsers == null)
                _data.HeadshotDisabledUsers = new List<ulong>();

            if (!_data.HeadshotDisabledUsers.Contains(player.userID))
            {
                _data.HeadshotDisabledUsers.Add(player.userID);
                PrintToChat(player, lang.GetMessage("HeadshotDisabled", this, player.UserIDString));
            }
            else
            {
                _data.HeadshotDisabledUsers.Remove(player.userID);
                PrintToChat(player, lang.GetMessage("HeadshotEnabled", this, player.UserIDString));
            }
            SaveData();
        }

        #endregion
    }

    public class HeadshotIconSettings
    {
        [Newtonsoft.Json.JsonProperty("Hit body color")]
        public string ColorBody { get; set; } = "1 1 1 1";

        [Newtonsoft.Json.JsonProperty("Hit Death body color")]
        public string ColorDeath { get; set; } = "1 0 0 1";

        [Newtonsoft.Json.JsonProperty("Show damage")]
        public bool ShowDamage { get; set; }

        [Newtonsoft.Json.JsonProperty("Show death skull")]
        public bool ShowDeathSkull { get; set; } = true;

        [Newtonsoft.Json.JsonProperty("Show hits/deaths on NPC (Bears, wolfs, etc.)")]
        public bool ShowNpc { get; set; }

        [Newtonsoft.Json.JsonProperty("Time to destroy")]
        public float TimeToDestroy { get; set; } = 0.45f;

        [Newtonsoft.Json.JsonProperty("Show icon on headshot (in addition to HitMarkers icon)")]
        public bool ShowOnHeadshot { get; set; }
    }
}
