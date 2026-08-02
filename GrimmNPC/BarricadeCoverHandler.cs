using System.Collections;
using System.Collections.Generic;
using Facepunch;
using Rust;
using Rust.Ai;
using UnityEngine;

namespace GrimmNPC
{
    /// <summary>
    /// NpcSpawn-style wooden double cover + syringe under fire (stock brain; no custom AI state machine).
    /// </summary>
    public static class BarricadeCoverHandler
    {
        private const uint BarricadeWoodCoverDeployablePrefabId = 2982625522U;
        private const string BarricadePrefab = "assets/prefabs/deployable/barricades/barricade.cover.wood_double.prefab";
        private const int BarricadeLosMask = 2097408; // Default | Construction (NpcSpawn)
        private static readonly int OverlapMask = unchecked((int)(uint)(1U << 0 | 1U << 8 | 1U << 10 | 1U << 15 | 1U << 16 | 1U << 17 | 1U << 21 | 1U << 27 | 1U << 31));
        private static readonly HashSet<string> BarricadeEntityShortnames = new HashSet<string>
        {
            "barricade.cover.wood_double", "barricade.sandbags", "barricade.concrete", "barricade.stone",
            "barricade.medieval", "barricade.metal", "barricade.woodwire", "barricade.wood", "icewall"
        };

        private static readonly Vector3 LocalPosNearBarricade = new Vector3(0f, 0f, 0.5f);
        private static readonly Collider[] OverlapBuf = new Collider[32];
        private static Deployable _deployableBoundsSource;

        public static void TryTick(ScientistNPC npc, CustomNpcData npcData, BaseAIBrain brain, AIState currentState, float now)
        {
            if (npc == null || npcData == null || brain == null || !npcData.EnableBarricadeCover) return;
            if (brain.Navigator != null && brain.Navigator.IsSwimming()) return;
            if (currentState != AIState.Combat && currentState != AIState.Chase) return;

            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return;

            var ws = SpecialWeaponsHandler.GetState(netId);
            if (ws == null) return;
            if (ws.BarricadeCoroutine != null || ws.IsPlacingBarricade) return;
            if (ws.IsHealing || ws.HealCoroutine != null) return;
            if (now < ws.NextBarricadeAllowedTime) return;

            if (!TryGetBrainMemoryTarget(brain, out BaseEntity targetEnt) || targetEnt is not BasePlayer)
                return;

            float maxHp = npcData.Health > 0f ? npcData.Health : npc.startHealth;
            if (maxHp <= 1f) return;
            float frac = npc.health / maxHp;
            if (frac > npcData.BarricadeMaxHealthFraction) return;

            Vector3 toTarget = targetEnt.transform.position - npc.transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude < npcData.BarricadeMinTargetDistance * npcData.BarricadeMinTargetDistance)
                return;

            if (IsTargetBehindBarricade(npc, targetEnt))
                return;

            string coverShort = string.IsNullOrEmpty(npcData.BarricadeCoverBeltShortname)
                ? "barricade.wood.cover"
                : npcData.BarricadeCoverBeltShortname;

            Item coverItem = FindBeltItem(npc, coverShort);
            Item syringe = FindBeltItem(npc, "syringe.medical");
            if (coverItem == null || syringe == null) return;

            Vector3 dir = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : new Vector3(npc.transform.forward.x, 0f, npc.transform.forward.z).normalized;
            if (!TryComputeBarricadePlacement(npc, dir, out Vector3 placePos, out Quaternion placeRot))
                return;

            ws.NextBarricadeAllowedTime = now + Mathf.Max(10f, npcData.BarricadeCooldownSeconds);
            ws.IsPlacingBarricade = true;
            ws.BarricadeCoroutine = ServerMgr.Instance.StartCoroutine(BarricadeAndHealRoutine(npc, npcData, brain, ws, coverItem, syringe, placePos, placeRot));
        }

        private static Item FindBeltItem(ScientistNPC npc, string shortname)
        {
            if (npc?.inventory?.containerBelt?.itemList == null) return null;
            foreach (var it in npc.inventory.containerBelt.itemList)
            {
                if (it?.info != null && it.info.shortname == shortname)
                    return it;
            }
            return null;
        }

        private static bool TryGetBrainMemoryTarget(BaseAIBrain brain, out BaseEntity ent)
        {
            ent = null;
            if (brain?.Events?.Memory == null) return false;
            int slot = brain.Events.CurrentInputMemorySlot;
            if (slot < 0) return false;
            ent = brain.Events.Memory.Entity?.Get(slot);
            return ent != null && !ent.IsDestroyed;
        }

        private static bool IsTargetBehindBarricade(ScientistNPC npc, BaseEntity target)
        {
            if (npc?.eyes == null || target == null) return false;
            Vector3 eyes = npc.eyes.position;
            Vector3 to = target.transform.position - eyes;
            float dist = to.magnitude;
            if (dist < 0.5f) return false;
            Vector3 n = to / dist;
            if (!Physics.Raycast(eyes, n, out RaycastHit hit, dist, BarricadeLosMask))
                return false;
            BaseEntity be = hit.GetEntity();
            return be != null && BarricadeEntityShortnames.Contains(be.ShortPrefabName);
        }

        private static Bounds GetDeployableBounds()
        {
            if (_deployableBoundsSource == null)
                _deployableBoundsSource = PrefabAttribute.server.Find<Deployable>(BarricadeWoodCoverDeployablePrefabId);
            if (_deployableBoundsSource != null)
                return _deployableBoundsSource.bounds;
            return new Bounds(Vector3.zero, new Vector3(2f, 1.5f, 0.4f));
        }

        private static bool TryComputeBarricadePlacement(ScientistNPC npc, Vector3 direction, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            Vector3 p = npc.transform.position + direction * 1f;
            rot = Quaternion.LookRotation(direction, Vector3.up) * Quaternion.AngleAxis(180f, Vector3.up);
            if (!Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3f, 8454144))
                return false;
            pos = hit.point;
            Bounds b = GetDeployableBounds();
            int count = Physics.OverlapBoxNonAlloc(pos + rot * b.center, b.extents, OverlapBuf, rot, OverlapMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = OverlapBuf[i];
                if (!c || c.isTrigger || c == hit.collider) continue;
                BaseEntity other = c.ToBaseEntity();
                if (other is ScientistNPC s && s == npc) continue;
                return false;
            }
            return true;
        }

        private static IEnumerator BarricadeAndHealRoutine(ScientistNPC npc, CustomNpcData npcData, BaseAIBrain brain, SpecialWeaponsHandler.WeaponState ws, Item coverItem, Item syringe, Vector3 globalPos, Quaternion globalRot)
        {
            try
            {
                if (npc == null || npc.IsDestroyed || coverItem == null)
                    yield break;

                if (coverItem.amount <= 1) coverItem.Remove();
                else
                {
                    coverItem.amount--;
                    coverItem.MarkDirty();
                }

                var ent = GameManager.server.CreateEntity(BarricadePrefab, globalPos, globalRot) as Barricade;
                if (ent == null)
                    yield break;

                ent.OwnerID = npc.userID;
                ent.enableSaving = false;
                ent.Spawn();

                Vector3 near = ent.transform.TransformPoint(LocalPosNearBarricade);
                if (brain?.Navigator != null)
                    brain.Navigator.SetDestination(near, BaseNavigator.NavigationSpeed.Fast);

                yield return CoroutineEx.waitForSeconds(0.75f);

                if (npc == null || npc.IsDestroyed || syringe == null || syringe.amount <= 0)
                    yield break;

                npc.UpdateActiveItem(syringe.uid);
                yield return CoroutineEx.waitForSeconds(1.25f);

                var tool = syringe.GetHeldEntity() as MedicalTool;
                if (tool != null && !tool.IsDestroyed)
                    tool.ServerUse();

                float maxHp = npcData.Health > 0f ? npcData.Health : npc.startHealth;
                float add = 15f * Mathf.Max(0.1f, npcData.HealingScale);
                float nh = npc.health + add;
                if (nh > maxHp) nh = maxHp;
                npc.health = nh;

                yield return CoroutineEx.waitForSeconds(2f);
            }
            finally
            {
                if (npc != null && !npc.IsDestroyed)
                    npc.EquipWeapon();
                ws.IsPlacingBarricade = false;
                ws.BarricadeCoroutine = null;
                float cd = npcData != null ? npcData.SyringeCooldownSeconds : 4f;
                ws.NextSyringeAllowedTime = Time.time + Mathf.Max(2f, cd);
            }
        }
    }
}
