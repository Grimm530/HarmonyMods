using Facepunch;
using HarmonyLib;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rust;
using Rust.Ai.Gen2;
using Rust.Ai.Gen2.Nav;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
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

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int index = UnityEngine.Random.Range(0, i + 1);
                T value = list[i];
                list[i] = list[index];
                list[index] = value;
            }
        }

        public RaidableBase OpenEvent(RandomBase rb)
        {
            var go = new GameObject(Name);
            var raid = go.AddComponent<RaidableBase>();

            rb.raid = raid;
            raid.rb = rb; // fixes CheckSubscribe bug when paste times out and rb is never set
            raid.Options = rb.options;
            raid.spawns = rb.spawns;
            raid.payments = rb.payments;
            raid.go = go;
            raid.Instance = this;
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
            raid.IsWaterSpawn = rb.IsWaterSpawn;
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
            else if (raid.Options.Siege.Only || raid.Options.RestrictByWorkbenchLevel(MaxConsideredWorkbenchLevel))
            {
                Subscribe(nameof(OnActiveItemChanged));
            }

            if (raid.BlacklistedCommands.Count > 0)
            {
                Subscribe(nameof(OnPlayerCommand));
                Subscribe(nameof(OnServerCommand));
            }

            if (IsPVE())
            {
                Subscribe(nameof(CanEntityTrapTrigger));
                Subscribe(nameof(CanEntityBeTargeted));
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

            if (Raids.Count == 1)
            {
                harmonyEngine.SetEnabled(HarmonyEngine.PatchGroup.RaidWindow, true);
                Subscribe(nameof(OnPlayerRespawn));
                CheckPlayersNearEvents();
            }

            raid.CheckPaste();
            raid.SendDronePatrol(rb);
            raid.SetupCollider();

            return raid;
        }

        #endregion

    }
}
