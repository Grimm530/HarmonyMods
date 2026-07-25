using System.Collections.Generic;
using UnityEngine;

namespace Convoy
{
    /// <summary>
    /// Oxide Convoy ZoneController (visual portion): dome + colored border spheres around the
    /// stop zone when the convoy is attacked / stopped.
    /// </summary>
    public sealed class ConvoyZoneController : FacepunchBehaviour
    {
        private static ConvoyZoneController _zoneController;
        private readonly HashSet<BaseEntity> _spheres = new HashSet<BaseEntity>();

        public static void CreateZone(BasePlayer externalOwner = null)
        {
            TryDeleteZone();

            var ec = EventController.Instance;
            if (ec == null) return;

            Vector3 position = ec.GetEventPosition();
            if (position == Vector3.zero) return;

            var go = new GameObject("Convoy_EventZone")
            {
                transform = { position = position },
                layer = (int)Rust.Layer.Reserved1
            };

            _zoneController = go.AddComponent<ConvoyZoneController>();
            _zoneController.Init();

            // Oxide ZoneController.Init → PveModeManager.CreatePveModeZone
            if (PveModeManager.IsPveModeReady())
                PveModeManager.CreatePveModeZone(position, externalOwner);
        }

        public static void TryDeleteZone()
        {
            if (PveModeManager.IsPveModeReady())
                PveModeManager.DeletePveModeZone();

            if (_zoneController != null)
                _zoneController.DeleteZone();
        }

        public static bool IsActive => _zoneController != null;

        private void Init()
        {
            CreateSpheres();
        }

        private void CreateSpheres()
        {
            var cfg = ConvoyMod.Instance?.FullConfig?.ZoneConfig;
            if (cfg == null) return;

            var ec = EventController.Instance;
            float zoneRadius = ec?.EventConfig?.ZoneRadius > 0f
                ? ec.EventConfig.ZoneRadius
                : 50f;

            if (cfg.IsDome)
            {
                int darkening = cfg.Darkening > 0 ? cfg.Darkening : 1;
                for (int i = 0; i < darkening; i++)
                    CreateSphere("assets/prefabs/visualization/sphere.prefab", zoneRadius);
            }

            if (cfg.IsColoredBorder)
            {
                string spherePrefab = cfg.BorderColor == 0
                    ? "assets/bundled/prefabs/modding/events/twitch/br_sphere.prefab"
                    : cfg.BorderColor == 1
                        ? "assets/bundled/prefabs/modding/events/twitch/br_sphere_green.prefab"
                        : cfg.BorderColor == 2
                            ? "assets/bundled/prefabs/modding/events/twitch/br_sphere_purple.prefab"
                            : "assets/bundled/prefabs/modding/events/twitch/br_sphere_red.prefab";

                int brightness = cfg.Brightness > 0 ? cfg.Brightness : 1;
                for (int i = 0; i < brightness; i++)
                    CreateSphere(spherePrefab, zoneRadius);
            }
        }

        private void CreateSphere(string prefabName, float zoneRadius)
        {
            BaseEntity sphere = GameManager.server.CreateEntity(prefabName, transform.position);
            if (sphere == null) return;

            SphereEntity entity = sphere.GetComponent<SphereEntity>();
            if (entity != null)
            {
                entity.currentRadius = zoneRadius * 2f;
                entity.lerpSpeed = 0f;
            }

            sphere.enableSaving = false;
            sphere.Spawn();
            _spheres.Add(sphere);
        }

        private void DeleteZone()
        {
            foreach (BaseEntity sphere in _spheres)
            {
                if (sphere != null && !sphere.IsDestroyed)
                    sphere.Kill();
            }
            _spheres.Clear();

            if (_zoneController == this)
                _zoneController = null;

            Destroy(gameObject);
        }
    }
}
