using Facepunch;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using static RaidableBases.RaidableBasesExtensionMethods.ExtensionMethods;

namespace RaidableBases
{
    public partial class RaidableBases
    {

        #region Spawn

        private static void Shuffle<T>(IList<T> list) // Fisher-Yates shuffle
        {
            int count = list.Count;
            int n = count;
            while (n-- > 0)
            {
                int k = UnityEngine.Random.Range(0, count);
                int j = UnityEngine.Random.Range(0, count);
                T value = list[k];
                list[k] = list[j];
                list[j] = value;
            }
        }

        public RaidableBase OpenEvent(RandomBase rb)
        {
            var go = new GameObject(Name);
            var raid = go.AddComponent<RaidableBase>();

            raid.go = go;
            raid.Instance = this;
            raid.Options = rb.options;
            raid.ProtectionRadius = rb.options.ProtectionRadius(rb.type);
            raid.SqrProtectionRadius = raid.ProtectionRadiusSqr(0);
            raid.markerName = raid.MarkerName;
            raid.spawnDateTime = DateTime.Now;
            raid.stability = rb.stability;
            raid.SetAllowPVP(rb);
            raid.Location = rb.Position;
            raid.LocationXZ3D = rb.Position.XZ3D();
            raid.BaseName = rb.BaseName;
            raid.BaseHeight = rb.baseHeight;
            raid.ProfileName = rb.Profile.ProfileName;
            raid.IsLoading = true;
            raid.loadTime = Time.time;
            raid.InitiateTurretOnSpawn = rb.options.AutoTurret.InitiateOnSpawn;

            if (rb.type == RaidableType.Purchased)
            {
                raid.ownerId = rb.payments.userid;
                raid.ownerName = rb.payments.username;
            }

            foreach (var multiplier in raid.Options.PlayerDamageMultiplier)
            {
                float amount = multiplier.amount;
                if (amount == 1f) continue;
                DamageType index = multiplier.index;
                if (index == DamageType.Generic) continue;
                raid.PlayerDamageMultiplier.Add(new() { index = index, amount = amount });
            }

            if (!raid.Options.MLRS)
            {
                Subscribe(nameof(OnMlrsFire));
            }

            if ((config.Settings.NoWizardryPVP && raid.AllowPVP || config.Settings.NoWizardryPVE && !raid.AllowPVP) && Wizardry.CanCall())
            {
                Subscribe(nameof(OnActiveItemChanged));
            }
            else if ((config.Settings.NoArcheryPVP && raid.AllowPVP || config.Settings.NoArcheryPVE && !raid.AllowPVP) && Archery.CanCall())
            {
                Subscribe(nameof(OnActiveItemChanged));
            }
            else if (raid.Options.Siege.Only)
            {
                Subscribe(nameof(OnActiveItemChanged));
            }

            if (raid.BlacklistedCommands.Count > 0)
            {
                Subscribe(nameof(OnPlayerCommand));
                Subscribe(nameof(OnServerCommand));
            }

            // Harmony always needs CanEntityBeTargeted (Targeting_Patches). Oxide only
            // subscribed it on PVE plugins; without it raid turrets/traps go blind under Harmony.
            Subscribe(nameof(CanEntityBeTargeted));
            if (IsPVE())
            {
                Subscribe(nameof(CanEntityTrapTrigger));
            }
            else
            {
                Subscribe(nameof(OnTrapTrigger));
            }

            SubscribeDamageHook();
            Subscribe(nameof(OnSamSiteTargetScan));
            Subscribe(nameof(OnNearbyTurretsScan));
            Subscribe(nameof(OnInterferenceUpdate));
            Subscribe(nameof(OnStructureUpgrade));
            Subscribe(nameof(OnEntityEnter));
            Subscribe(nameof(CanBuild));
            Subscribe(nameof(OnEntitySpawned));

            data.TotalEvents++;
            raid._undoLimit = Mathf.Clamp(raid.Options.Setup.DespawnLimit, 1, 500);

            Raids.Add(raid);

            raid.CheckPaste();
            raid.SendDronePatrol(rb);
            raid.SetupCollider();

            if (Raids.Count == 1)
            {
                CheckPlayersNearEvents();
            }

            return raid;
        }

        #endregion

    }
}
