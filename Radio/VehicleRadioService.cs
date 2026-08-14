using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace RadioHarmony
{
    public class VehicleRadioService
    {
        const string BoomboxPrefab = "assets/prefabs/voiceaudio/boombox/boombox.deployed.prefab";
        readonly RadioMod _mod;
        readonly string _dataPath;
        Dictionary<string, string> _vehiclesWithRadio = new Dictionary<string, string>();

        public VehicleRadioService(RadioMod mod, string root)
        {
            _mod = mod;
            string dir = Path.Combine(root, "HarmonyData", "Radio");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            _dataPath = Path.Combine(dir, "VehicleRadio.json");
        }

        VehicleRadioConfig Cfg => _mod.Config?.VehicleRadio ?? new VehicleRadioConfig();

        public void LoadData()
        {
            try
            {
                if (File.Exists(_dataPath))
                {
                    var data = JsonConvert.DeserializeObject<Dictionary<string, object>>(File.ReadAllText(_dataPath));
                    if (data != null && data.TryGetValue("radios", out var radios))
                    {
                        var json = JsonConvert.SerializeObject(radios);
                        _vehiclesWithRadio = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                                            ?? new Dictionary<string, string>();
                    }
                }
            }
            catch
            {
                _vehiclesWithRadio = new Dictionary<string, string>();
            }
        }

        public void SaveData()
        {
            try
            {
                var data = new Dictionary<string, object> { ["radios"] = _vehiclesWithRadio };
                File.WriteAllText(_dataPath, JsonConvert.SerializeObject(data, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Radio] VehicleRadio save: " + ex.Message);
            }
        }

        public void ValidateAndRestore()
        {
            var invalid = new List<string>();
            var snapshot = new List<KeyValuePair<string, string>>(_vehiclesWithRadio);
            foreach (var entry in snapshot)
            {
                try
                {
                    if (entry.Key.EndsWith("_speaker")) continue;
                    var vehicleId = new NetworkableId(ulong.Parse(entry.Key));
                    var radioId = new NetworkableId(ulong.Parse(entry.Value));
                    var vehicle = BaseNetworkable.serverEntities.Find(vehicleId) as BaseEntity;
                    var radio = BaseNetworkable.serverEntities.Find(radioId) as BaseEntity;
                    if (vehicle == null || vehicle.IsDestroyed)
                    {
                        invalid.Add(entry.Key);
                        continue;
                    }
                    if (radio == null || radio.IsDestroyed)
                    {
                        bool isMini = vehicle is Minicopter;
                        bool isAtk = vehicle is AttackHelicopter;
                        AddRadioToVehicle(vehicle, null, isMini, isAtk);
                    }
                }
                catch
                {
                    invalid.Add(entry.Key);
                }
            }
            foreach (var key in invalid) _vehiclesWithRadio.Remove(key);
            if (invalid.Count > 0) SaveData();
        }

        public void OnVehicleSpawned(BaseNetworkable entity)
        {
            if (!(entity is Minicopter) && !(entity is AttackHelicopter) && !(entity is Tugboat))
                return;
            var vehicle = entity as BaseEntity;
            if (vehicle == null || vehicle.IsDestroyed) return;
            if (!IsAllowed(vehicle)) return;
            if (HasRadioAttached(vehicle)) return;
            if (!Cfg.AutoInstallOnSpawn) return;
            bool isMini = vehicle is Minicopter;
            bool isAtk = vehicle is AttackHelicopter;
            AddRadioToVehicle(vehicle, null, isMini, isAtk);
        }

        public void OnEntityKilled(BaseNetworkable entity)
        {
            if (entity?.net == null) return;
            string vehicleIdStr = entity.net.ID.ToString();
            if (!_vehiclesWithRadio.TryGetValue(vehicleIdStr, out string radioIdStr)) return;
            try
            {
                var radioId = new NetworkableId(ulong.Parse(radioIdStr));
                var radio = BaseNetworkable.serverEntities.Find(radioId) as BaseEntity;
                if (radio != null && !radio.IsDestroyed)
                    radio.Kill();
            }
            catch { }
            _vehiclesWithRadio.Remove(vehicleIdStr);
            SaveData();
        }

        public void CmdInstall(BasePlayer player)
        {
            var cfg = Cfg;
            if (!PermissionsBridge.UserHasPermission(player.UserIDString, cfg.Permission) && !player.IsAdmin)
            {
                player.ChatMessage(cfg.Messages.NoPermission);
                return;
            }
            var vehicle = player.GetMountedVehicle();
            if (vehicle == null)
            {
                player.ChatMessage(cfg.Messages.NotInVehicle);
                return;
            }
            if (!IsAllowed(vehicle))
            {
                player.ChatMessage(cfg.Messages.UnsupportedVehicle);
                return;
            }
            if (HasRadioAttached(vehicle))
            {
                player.ChatMessage(cfg.Messages.AlreadyHasRadio);
                return;
            }
            AddRadioToVehicle(vehicle, player, vehicle is Minicopter, vehicle is AttackHelicopter);
            player.ChatMessage(cfg.Messages.RadioInstalled);
        }

        public void CmdRemove(BasePlayer player)
        {
            var cfg = Cfg;
            if (!PermissionsBridge.UserHasPermission(player.UserIDString, cfg.Permission) && !player.IsAdmin)
            {
                player.ChatMessage(cfg.Messages.NoPermission);
                return;
            }
            var vehicle = player.GetMountedVehicle();
            if (vehicle == null)
            {
                player.ChatMessage(cfg.Messages.NotInVehicle);
                return;
            }
            var radioEntity = FindRadioOnVehicle(vehicle);
            if (radioEntity == null)
            {
                player.ChatMessage(cfg.Messages.NoRadio);
                return;
            }
            radioEntity.Kill();
            _vehiclesWithRadio.Remove(vehicle.net.ID.ToString());
            SaveData();
            player.ChatMessage(cfg.Messages.RadioRemoved);
        }

        bool IsAllowed(BaseEntity vehicle)
        {
            var cfg = Cfg;
            if (vehicle is Minicopter) return cfg.Vehicles.AllowMinicopters;
            if (vehicle is AttackHelicopter) return cfg.Vehicles.AllowAttackHelicopters;
            if (vehicle is Tugboat) return cfg.Vehicles.AllowTugboats;
            return false;
        }

        bool HasRadioAttached(BaseEntity vehicle) => FindRadioOnVehicle(vehicle) != null;

        BaseEntity FindRadioOnVehicle(BaseEntity vehicle)
        {
            if (vehicle?.net == null) return null;
            if (_vehiclesWithRadio.TryGetValue(vehicle.net.ID.ToString(), out string radioIdStr))
            {
                var radioId = new NetworkableId(ulong.Parse(radioIdStr));
                var radio = BaseNetworkable.serverEntities.Find(radioId) as BaseEntity;
                if (radio != null && !radio.IsDestroyed) return radio;
                _vehiclesWithRadio.Remove(vehicle.net.ID.ToString());
            }
            foreach (var entity in vehicle.GetComponentsInChildren<BaseEntity>())
            {
                if (entity != null && !entity.IsDestroyed && entity.PrefabName != null && entity.PrefabName.Contains("boombox"))
                {
                    _vehiclesWithRadio[vehicle.net.ID.ToString()] = entity.net.ID.ToString();
                    SaveData();
                    return entity;
                }
            }
            return null;
        }

        void AddRadioToVehicle(BaseEntity vehicle, BasePlayer player, bool isMinicopter, bool isAttackHelicopter)
        {
            if (HasRadioAttached(vehicle)) return;
            var radioEntity = GameManager.server.CreateEntity(BoomboxPrefab, vehicle.transform.position) as BaseEntity;
            if (radioEntity == null)
            {
                Debug.LogWarning("[Radio] Failed to create boombox");
                return;
            }

            radioEntity.SetParent(vehicle, "");
            var cfg = Cfg.Vehicles;
            if (!isMinicopter && !isAttackHelicopter)
            {
                var tug = cfg.TugboatPosition;
                radioEntity.transform.localPosition = new Vector3(tug.X, tug.Y > 0 ? tug.Y : 5f, tug.Z > 0 ? tug.Z : 5f);
                radioEntity.transform.localRotation = Quaternion.Euler(0f, tug.RotationY, 0f);
            }
            else
            {
                var pos = isMinicopter ? cfg.MinicopterPosition : cfg.AttackHelicopterPosition;
                radioEntity.transform.localPosition = new Vector3(pos.X, pos.Y, pos.Z);
                radioEntity.transform.localRotation = Quaternion.Euler(0f, pos.RotationY, 0f);
            }

            if (player != null) radioEntity.OwnerID = player.userID;
            radioEntity.enableSaving = true;
            radioEntity.Spawn();

            var ioPrefab = radioEntity as IOEntity;
            if (ioPrefab != null)
            {
                ioPrefab.SetFlag(BaseEntity.Flags.Reserved8, true);
                ioPrefab.SetFlag(BaseEntity.Flags.On, true);
            }

            _vehiclesWithRadio[vehicle.net.ID.ToString()] = radioEntity.net.ID.ToString();
            SaveData();
        }
    }
}
