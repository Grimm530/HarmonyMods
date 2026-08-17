using System;
using System.Collections;
using System.Collections.Generic;
using Facepunch;
using Rust;
using Rust.Ai;
using UnityEngine;

namespace GrimmNPC
{
    /// <summary>
    /// Centralized special weapon handling for NPCs (rockets, grenade launchers, flamethrowers, bows).
    /// Weapon behavior is shared across combat and raiding to keep logic orthogonal.
    /// </summary>
    public static class SpecialWeaponsHandler
    {
        private const float RocketMinRange = 6f;
        private const float RocketMaxRange = 30f;
        private const float RocketPreFireDelay = 1.5f;
        private const float RocketReloadDuration = 6f;
        private const float RocketAimCone = 2.25f;

        private const float GrenadeLauncherMinRange = 6f;
        private const float GrenadeLauncherMaxRange = 25f;
        private const float GrenadeLauncherPreFireDelay = 0.25f;
        private const float GrenadeLauncherFireInterval = 0.6f;
        private const float GrenadeLauncherReloadDuration = 8f;
        private const int GrenadeLauncherMagazineSize = 6;
        private const float GrenadeLauncherAimCone = 0.675f;

        private const float FlameThrowerBurstDuration = 0.25f;
        private const float FlameThrowerReloadDuration = 4f;
        private const float FlameThrowerFireInterval = 0.4f;

        private const float BowDrawDelay = 0.5f;
        private const float CompoundBowDrawDelay = 0.7f;

        // 🧨 Satchel behavior improvements (based on RaidingZombies pattern)
        private const float SatchelMinRange = 2f; // Minimum throw distance (melee range)
        private const float SatchelMaxRange = 15f; // Increased allowable throw distance (was ~8m)
        private const float SatchelOptimalRange = 8f; // Optimal throw radius (approach this, not melee range)
        private const float SatchelApproachDistance = 1.5f; // Distance to approach if too far/close
        private const float SatchelWindUpDelay = 1.5f; // Wind-up delay before throw (like RaidingZombies)
        private const float SatchelThrowCooldown = 6f; // Cooldown between throws
        private const float SatchelAimStabilizeDelay = 0.1f; // Delay to stabilize aim before throw
        
        // 🧨 Satchel cycle timing
        private const int SatchelsPerCycle = 3; // Number of satchels to throw per cycle
        private const float SatchelExplosionWaitTime = 8f; // Wait for satchels to explode (8 seconds)
        private const float MeleeAttackDuration = 30f; // Melee attack for 30 seconds

        private static bool LogWeapons => GrimmNPC.ShouldLogCombatWeapons();
        private const float RetreatDuration = 10f; // Time to retreat to optimal range (satchels take time to explode)
        private const float RetreatDistance = 12f; // Distance to retreat to (safe distance from explosions)

        private const int WeaponRaycastMask = 1236478737;

        public class WeaponState
        {
            public bool IsFiringRocket;
            public Coroutine RocketCoroutine;

            // Raid-specific flags (like NpcSpawn)
            public bool IsReloadRocketLauncher;
            public bool IsFireRocketLauncher;
            public bool IsReloadC4;
            public bool IsFireC4;
            public Coroutine FireRocketLauncherCoroutine;
            public Coroutine FireC4Coroutine;

            public float GrenadeLauncherReloadUntil;
            public float NextGrenadeLauncherFireTime;
            public int GrenadeLauncherAmmoRemaining = GrenadeLauncherMagazineSize;
            public string GrenadeLauncherAmmoType = "40mm_grenade_he";
            public Coroutine GrenadeLauncherCoroutine;

            public float FlameThrowerReloadUntil;
            public bool IsFlameThrowerActive; // Track if flamethrower is currently firing
            public bool FlameThrowerSwappedWeapon; // Track if we swapped weapons during reload

            public bool IsDrawingBow;
            public float NextBowFireTime;
            public Coroutine BowCoroutine;

            // 🧨 Satchel throwing state (coroutine pattern like RaidingZombies)
            public bool IsThrowingSatchel;
            public float NextSatchelThrowTime;
            public Coroutine SatchelCoroutine;
            public ThrownWeapon ThrownWeaponEntity;

            // 🧨 Satchel cycle state machine: THROW → WAIT_EXPLOSION → MELEE → RETREAT → repeat
            public enum SatchelCyclePhase
            {
                None,              // Not in satchel cycle
                Throwing,          // Currently throwing satchels
                WaitExplosion,     // Waiting for satchels to explode
                MeleeAttack,       // Melee attacking the base
                Retreat            // Retreating to optimal throw range
            }
            public SatchelCyclePhase SatchelPhase = SatchelCyclePhase.None;
            public float SatchelPhaseStartTime;
            public int SatchelsThrownThisCycle;
            public float LastSatchelThrowTime;
            public float MeleeAttackEndTime;

            public ItemId PreviousActiveItemUid;

            // 🩹 Healing state (like NpcSpawn)
            public bool IsHealing;
            public Coroutine HealCoroutine;
            /// <summary>Time.time after which another syringe heal may start.</summary>
            public float NextSyringeAllowedTime;
            /// <summary>Wooden cover barricade deploy (NpcSpawn-style).</summary>
            public bool IsPlacingBarricade;
            public Coroutine BarricadeCoroutine;
            public float NextBarricadeAllowedTime;
        }

        private static readonly Dictionary<ulong, WeaponState> WeaponStates = new Dictionary<ulong, WeaponState>(512);

        private static bool StopNavigator(ScientistNPC npc, BaseAIBrain brain, string methodName)
        {
            if (npc == null || brain?.Navigator == null)
                return false;

            var agent = brain.Navigator.Agent;
            bool isActiveAndEnabled = agent != null && agent.isActiveAndEnabled;
            bool isOnNavMesh = agent != null && agent.isOnNavMesh;
            if (!isActiveAndEnabled || !isOnNavMesh)
            {
                ulong netId = npc.net?.ID.Value ?? 0;
                GrimmNPC.LogNavMeshFailure(
                    "StopBlocked",
                    $"{methodName} netId={netId} active={isActiveAndEnabled} onNavMesh={isOnNavMesh} pos={npc.transform.position}",
                    netId);
                GrimmNPC.LogCombatFailure(
                    "ActionBlocked",
                    $"{methodName} stop skipped (invalid nav state) netId={netId}",
                    netId);
                return false;
            }

            brain.Navigator.Stop();
            return true;
        }

        /// <summary>
        /// Handles combat weapon attacks for ALL NPCs against players (not raiding).
        /// Allows NPCs to use all weapon types (rockets, grenades, flamethrowers, bows, F1 grenades) during combat with players.
        /// CRITICAL: Firing rockets at players is NOT raiding - NPCs should always be able to fire at players.
        /// </summary>
        public static bool HandleCombatAttack(ScientistNPC npc, BaseEntity target)
        {
            if (npc == null || target == null) return false;

            ulong nid = npc.net?.ID.Value ?? 0;
            if (nid != 0)
            {
                var ws = GetState(nid);
                if (ws != null && (ws.IsHealing || ws.IsPlacingBarricade))
                    return false;
            }

            var brain = npc.Brain;
            if (brain == null || brain.Navigator == null) return false;

            // Only handle player targets for combat (not structures)
            BasePlayer targetPlayer = target as BasePlayer;
            if (targetPlayer == null) return false;

            // Calculate distance first (needed for weapon selection)
            float distance = Vector3.Distance(npc.transform.position, target.transform.position);

            // Use HumanNPC's CanSeeTarget for LOS check (more accurate than our custom method)
            bool hasLOS = false;
            try
            {
                var canSeeTargetMethod = npc.GetType().GetMethod("CanSeeTarget",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (canSeeTargetMethod != null)
                {
                    hasLOS = (bool)canSeeTargetMethod.Invoke(npc, new object[] { target });
                }
                else
                {
                    hasLOS = HasLineOfSight(npc, target);
                }
            }
            catch
            {
                hasLOS = HasLineOfSight(npc, target);
            }

            if (!hasLOS)
            {
                // No LOS - can't fire (but this is OK, NPC should use other weapons or move)
                return false;
            }

            // CRITICAL: Remove engagement range check - it's too restrictive
            // NPCs should be able to fire at any distance they can see the target
            // Weapon selection will handle range limits (rocket < 30m, etc.)
            
            // Select appropriate weapon based on distance
            WeaponChoice choice = SelectCombatWeapon(npc, target, distance);

            if (choice.Kind == WeaponKind.None)
            {
                // No weapon selected - this is OK, might be out of range for special weapons
                // Fallback will be handled by ExecuteCombatWeapon
                return false;
            }

            // Execute weapon attack
            bool result = ExecuteCombatWeapon(npc, target, choice, distance);
            
            // Debug logging (throttled)
            if (LogWeapons && Time.frameCount % 120 == 0)
            {
                UnityEngine.Debug.Log($"[GrimmNPC Combat] NPC {npc.net?.ID.Value ?? 0} HandleCombatAttack: " +
                    $"target={targetPlayer.displayName}, distance={distance:F1}m, hasLOS={hasLOS}, " +
                    $"weapon={choice.Kind}, handled={result}");
            }
            
            return result;
        }

        /// <summary>
        /// Selects appropriate combat weapon based on distance.
        /// Priority: Rocket Launcher (< 30m) > Grenade Launcher (< 25m) > Standard Projectile (if NPC has flamethrower and player is at range) > Flamethrower (close range) > Bow > F1 Grenade (15-50m) > Melee
        /// CRITICAL: If NPC has flamethrower and player is at range, prefer ranged weapon first (NPC will push in while shooting).
        /// </summary>
        private static WeaponChoice SelectCombatWeapon(ScientistNPC npc, BaseEntity target, float distance)
        {
            if (npc == null) return new WeaponChoice { Kind = WeaponKind.None, AttackRange = 0f };

            // Check if NPC has flamethrower (melee weapon)
            bool hasFlamethrower = HasFlameThrower(npc);
            float flamethrowerRange = hasFlamethrower ? GetFlameThrowerRange(npc) : 0f;

            // Rocket launcher: only max range check (< 30m), no minimum
            if (HasRocketLauncher(npc) && distance <= RocketMaxRange)
            {
                if (LogWeapons && Time.frameCount % 120 == 0)
                    UnityEngine.Debug.Log($"[GrimmNPC Weapon] NPC {npc.net?.ID.Value ?? 0} selected Rocket Launcher (distance={distance:F1}m)");
                return new WeaponChoice { Kind = WeaponKind.RocketLauncher, AttackRange = RocketMaxRange };
            }

            // Grenade launcher: < 25m (no minimum)
            if (HasGrenadeLauncher(npc) && distance <= GrenadeLauncherMaxRange)
                return new WeaponChoice { Kind = WeaponKind.GrenadeLauncher, AttackRange = GrenadeLauncherMaxRange };

            // CRITICAL: If NPC has flamethrower and player is at range, prefer ranged weapon first
            // This allows NPC to push in while shooting (user requirement)
            if (hasFlamethrower && distance > flamethrowerRange)
            {
                // Player is at range - use ranged weapon to push in while shooting
                // Bow: any range (compound bow preferred)
                Item bowItem = FindBeltItem(npc, item =>
                {
                    string shortName = item.info?.shortname ?? string.Empty;
                    return IsBowWeapon(shortName);
                });
                if (bowItem != null)
                    return new WeaponChoice { Kind = WeaponKind.BaseProjectile, AttackRange = 100f }; // Bows handled via BaseProjectile

                // Standard projectile weapons (rifles, pistols, etc.)
                Item standardWeaponPush = FindBeltItem(npc, item =>
                {
                    if (item == null || item.info == null) return false;
                    string shortName = item.info.shortname ?? string.Empty;
                    AttackEntity attackEntity = item.GetHeldEntity() as AttackEntity;
                    return attackEntity is BaseProjectile && !IsSpecialRangedWeapon(shortName);
                });
                if (standardWeaponPush != null)
                {
                    if (LogWeapons && Time.frameCount % 120 == 0)
                        UnityEngine.Debug.Log($"[GrimmNPC Weapon] NPC {npc.net?.ID.Value ?? 0} selected Standard Projectile to push in (has flamethrower, distance={distance:F1}m, weapon={standardWeaponPush.info.shortname})");
                    return new WeaponChoice { Kind = WeaponKind.BaseProjectile, AttackRange = 100f };
                }
            }

            // Flamethrower: close range (only if in range)
            if (hasFlamethrower && distance <= flamethrowerRange)
                return new WeaponChoice { Kind = WeaponKind.FlameThrower, AttackRange = flamethrowerRange };

            // Bow: any range (compound bow preferred) - only if no flamethrower or already checked
            if (!hasFlamethrower || distance <= flamethrowerRange)
            {
                Item bowItem = FindBeltItem(npc, item =>
                {
                    string shortName = item.info?.shortname ?? string.Empty;
                    return IsBowWeapon(shortName);
                });
                if (bowItem != null)
                    return new WeaponChoice { Kind = WeaponKind.BaseProjectile, AttackRange = 100f }; // Bows handled via BaseProjectile
            }

            // CRITICAL: Standard projectile weapons (rifles, pistols, etc.) - ALWAYS available as fallback
            // This ensures NPCs can always fire even if special weapons are out of range
            Item standardWeaponFallback = FindBeltItem(npc, item =>
            {
                if (item == null || item.info == null) return false;
                string shortName = item.info.shortname ?? string.Empty;
                AttackEntity attackEntity = item.GetHeldEntity() as AttackEntity;
                return attackEntity is BaseProjectile && !IsSpecialRangedWeapon(shortName);
            });
            if (standardWeaponFallback != null)
            {
                if (LogWeapons && Time.frameCount % 120 == 0)
                    UnityEngine.Debug.Log($"[GrimmNPC Weapon] NPC {npc.net?.ID.Value ?? 0} selected Standard Projectile (distance={distance:F1}m, weapon={standardWeaponFallback.info.shortname})");
                return new WeaponChoice { Kind = WeaponKind.BaseProjectile, AttackRange = 100f };
            }

            // F1 grenade: 15-50m range (like BotReSpawn ThrowDistance)
            Item f1Grenade = FindBeltItem(npc, item =>
            {
                string shortName = item.info?.shortname ?? string.Empty;
                return shortName == "grenade.f1" || shortName == "grenade.beancan";
            });
            if (f1Grenade != null && distance >= 15f && distance <= 50f)
            {
                // Check vertical distance (like BotReSpawn)
                float verticalDiff = Mathf.Abs(target.transform.position.y - npc.transform.position.y);
                if (verticalDiff <= 20f)
                    return new WeaponChoice { Kind = WeaponKind.ThrownExplosive, AttackRange = 50f };
            }

            // Melee: close range
            if (distance <= 4f)
                return new WeaponChoice { Kind = WeaponKind.Melee, AttackRange = 4f };

            // No weapon found - this shouldn't happen if NPC has weapons
            if (LogWeapons && Time.frameCount % 120 == 0)
                UnityEngine.Debug.LogWarning($"[GrimmNPC Weapon] NPC {npc.net?.ID.Value ?? 0} no weapon selected (distance={distance:F1}m)");
            return new WeaponChoice { Kind = WeaponKind.None, AttackRange = 0f };
        }

        /// <summary>
        /// Executes combat weapon attack (non-raiding) with fallback to standard weapons if special weapons fail.
        /// </summary>
        private static bool ExecuteCombatWeapon(ScientistNPC npc, BaseEntity target, WeaponChoice choice, float distance)
        {
            ulong netId = npc.net?.ID.Value ?? 0;
            var state = GetState(netId);
            if (state == null) return false;

            // 💣 BOMBER: Check if NPC is a bomber with timed explosive weapon
            var npcData = GrimmNPC.GetNpcData(netId);
            if (npcData != null && npcData.IsBomber)
            {
                Item activeItem = npc.GetActiveItem();
                if (activeItem != null && activeItem.info != null && activeItem.info.shortname == "explosive.timed")
                {
                    // Bomber NPC using timed explosive - trigger explosion (like NpcSpawn CombatState)
                    Raid.ExplosionBomber(npc, target);
                    return true;
                }
            }

            bool handled = false;
            switch (choice.Kind)
            {
                case WeaponKind.RocketLauncher:
                    handled = FireRocketLauncher(npc, target as BaseCombatEntity, distance);
                    break;
                case WeaponKind.GrenadeLauncher:
                    handled = FireGrenadeLauncher(npc, target as BaseCombatEntity, distance);
                    break;
                case WeaponKind.FlameThrower:
                    handled = FireFlameThrower(npc, target as BaseCombatEntity, distance);
                    break;
                case WeaponKind.BaseProjectile:
                    // For bows, use special bow handling
                    Item activeItem = npc.GetActiveItem();
                    string shortName = activeItem?.info?.shortname ?? string.Empty;
                    if (IsBowWeapon(shortName))
                    {
                        handled = FireBow(npc, target as BaseCombatEntity, distance, 50f, shortName);
                    }
                    else
                    {
                        // Standard projectile - let vanilla AI handle it
                        handled = false; // Let vanilla combat handle standard weapons
                    }
                    break;
                case WeaponKind.ThrownExplosive:
                    // F1 grenade throwing (like BotReSpawn.Throw)
                    handled = ThrowF1Grenade(npc, target);
                    break;
                case WeaponKind.Melee:
                    handled = UseMeleeWeapon(npc, target as BaseCombatEntity, 25f);
                    break;
            }

            // CRITICAL: Fallback to standard weapons if special weapon failed (like ExecuteRaidWeapon does)
            // This ensures NPCs always use their LR300/other weapons when rocket launcher can't fire
            if (!handled)
            {
                // Find standard projectile weapon in belt (LR300, AK, etc.)
                Item standardWeapon = FindBeltItem(npc, item =>
                {
                    if (item == null || item.info == null) return false;
                    string shortName = item.info.shortname ?? string.Empty;
                    // Check if it's a standard projectile weapon (not special)
                    AttackEntity attackEntity = item.GetHeldEntity() as AttackEntity;
                    return attackEntity is BaseProjectile && !IsSpecialRangedWeapon(shortName);
                });
                
                if (standardWeapon != null)
                {
                    // Equip standard weapon and fire
                    EquipItem(npc, standardWeapon);
                    bool fallbackResult = FireBaseProjectile(npc, target as BaseCombatEntity, distance, 50f);
                    if (LogWeapons && Time.frameCount % 120 == 0)
                        UnityEngine.Debug.Log($"[GrimmNPC Combat] NPC {npc.net?.ID.Value ?? 0} fallback to standard weapon: {standardWeapon.info.shortname}, result={fallbackResult}");
                    return fallbackResult;
                }
                else if (distance <= 4f)
                {
                    // Fallback to melee if close
                    return UseMeleeWeapon(npc, target as BaseCombatEntity, 25f);
                }
                else
                {
                    if (LogWeapons && Time.frameCount % 120 == 0)
                        UnityEngine.Debug.LogWarning($"[GrimmNPC Combat] NPC {npc.net?.ID.Value ?? 0} no fallback weapon available (distance={distance:F1}m)");
                }
            }

            return handled;
        }

        /// <summary>
        /// Throws F1 grenade at target (like BotReSpawn.Throw).
        /// </summary>
        private static bool ThrowF1Grenade(ScientistNPC npc, BaseEntity target)
        {
            if (npc == null || target == null) return false;

            ulong netId = npc.net?.ID.Value ?? 0;
            var state = GetState(netId);
            if (state == null) return false;

            // Check cooldown
            if (Time.time < state.NextSatchelThrowTime)
                return false;

            // Find F1 grenade
            Item f1Grenade = FindBeltItem(npc, item =>
            {
                string shortName = item.info?.shortname ?? string.Empty;
                return shortName == "grenade.f1" || shortName == "grenade.beancan";
            });
            if (f1Grenade == null) return false;

            // Start throw coroutine
            state.IsThrowingSatchel = true;
            state.NextSatchelThrowTime = Time.time + 10f; // 10s cooldown like BotReSpawn
            state.SatchelCoroutine = ServerMgr.Instance.StartCoroutine(ProcessThrowF1Grenade(npc, target, f1Grenade, state));
            return true;
        }

        /// <summary>
        /// Coroutine for throwing F1 grenade (based on BotReSpawn pattern).
        /// </summary>
        private static IEnumerator ProcessThrowF1Grenade(ScientistNPC npc, BaseEntity target, Item grenadeItem, WeaponState state)
        {
            var brain = npc.Brain;
            if (brain == null || brain.Navigator == null)
            {
                state.IsThrowingSatchel = false;
                yield break;
            }

            try
            {
                // Equip grenade
                EquipItem(npc, grenadeItem);
                ThrownWeapon thrown = npc.GetActiveItem()?.GetHeldEntity() as ThrownWeapon;
                if (thrown == null)
                {
                    state.IsThrowingSatchel = false;
                    yield break;
                }

                // Wind-up delay (1.5s like BotReSpawn)
                yield return new WaitForSeconds(1.5f);

                // Check target validity
                if (target == null || target.IsDestroyed || npc == null || npc.IsDestroyed)
                {
                    state.IsThrowingSatchel = false;
                    yield break;
                }

                // Set aim direction (like BotReSpawn with precision)
                Vector3 targetPos = target.transform.position;
                Vector3 npcPos = npc.transform.position;
                
                // Add random spread for precision (like BotReSpawn Grenade_Precision_Percent)
                var rand = UnityEngine.Random.insideUnitCircle * 2f; // Small spread for F1
                Vector3 aimDir = ((targetPos + new Vector3(rand.x, 0, rand.y)) - npcPos).normalized;
                
                npc.SetAimDirection(aimDir);
                SetAim(npc, aimDir);

                // Throw grenade (like BotReSpawn.ServerThrow)
                thrown.SignalBroadcast(BaseEntity.Signal.Throw, string.Empty, null);
                BaseEntity entity = GameManager.server.CreateEntity(
                    thrown.prefabToThrow.resourcePath,
                    npc.eyes.position,
                    Quaternion.LookRotation(thrown.overrideAngle == Vector3.zero ? -aimDir : thrown.overrideAngle),
                    true
                );
                if (entity != null)
                {
                    entity.SetCreatorEntity(npc);
                    Vector3 throwDir = aimDir + (Quaternion.AngleAxis(10f, Vector3.zero) * Vector3.up);
                    float throwVelocity = CalculateThrowVelocity(npc.eyes.position, targetPos, throwDir);
                    if (float.IsNaN(throwVelocity))
                    {
                        throwDir = aimDir + (Quaternion.AngleAxis(20f, Vector3.zero) * Vector3.up);
                        throwVelocity = CalculateThrowVelocity(npc.eyes.position, targetPos, throwDir);
                        if (float.IsNaN(throwVelocity))
                            throwVelocity = 5f;
                    }
                    entity.SetVelocity(throwDir * throwVelocity);
                    if (thrown.tumbleVelocity > 0f)
                        entity.SetAngularVelocity(Vector3.zero * thrown.tumbleVelocity);
                    entity.Spawn();
                    thrown.StartAttackCooldown(thrown.repeatDelay);
                }

                // Restore weapon after delay
                yield return new WaitForSeconds(1f);
                if (npc != null && !npc.IsDestroyed)
                {
                    npc.EquipWeapon();
                }
            }
            finally
            {
                state.IsThrowingSatchel = false;
            }
        }

        /// <summary>
        /// Calculates throw velocity (like BotReSpawn.GetThrowVelocity).
        /// </summary>
        private static float CalculateThrowVelocity(Vector3 throwPos, Vector3 targetPos, Vector3 aimDir)
        {
            Vector3 vector3 = targetPos - throwPos;
            Vector2 vector2 = new Vector2(vector3.x, vector3.z);
            float single = vector2.magnitude;
            float single1 = vector3.y;
            vector2 = new Vector2(aimDir.x, aimDir.z);
            float single2 = vector2.magnitude;
            float single3 = aimDir.y;
            float single4 = Physics.gravity.y;
            return Mathf.Sqrt(0.5f * single4 * single * single / (single2 * (single2 * single1 - single3 * single)));
        }

        public static bool HandleRaidAttack(ScientistNPC npc, BaseCombatEntity target, RaidSettings settings)
        {
            if (npc == null || target == null || settings == null) return false;

            var brain = npc.Brain;
            if (brain == null || brain.Navigator == null) return false;

            float distance = Vector3.Distance(npc.transform.position, target.transform.position);
            WeaponChoice choice = SelectRaidWeapon(npc, settings, distance);

            if (distance > choice.AttackRange)
            {
                Vector3 raidDest = GrimmNPC.GetCombatApproachPosition(npc, target.transform.position, Mathf.Max(2f, choice.AttackRange * 0.5f));
                brain.Navigator.SetDestination(raidDest, BaseNavigator.NavigationSpeed.Normal);
                return true;
            }

            return ExecuteRaidWeapon(npc, target, settings, choice, distance);
        }

        private enum WeaponKind
        {
            RocketLauncher,
            GrenadeLauncher,
            FlameThrower,
            BaseProjectile,
            Melee,
            ThrownExplosive,
            None
        }

        private struct WeaponChoice
        {
            public WeaponKind Kind;
            public float AttackRange;
        }

        private static WeaponChoice SelectRaidWeapon(ScientistNPC npc, RaidSettings settings, float distance)
        {
            if (npc == null) return new WeaponChoice { Kind = WeaponKind.None, AttackRange = settings.AttackRangeMelee };

            ulong netId = npc.net?.ID.Value ?? 0;
            var state = GetState(netId);

            // 🧨 SATCHEL CYCLE: If in melee phase, prefer melee over satchels
            if (state != null && state.SatchelPhase == WeaponState.SatchelCyclePhase.MeleeAttack)
            {
                // In melee phase - use melee weapon
                return new WeaponChoice { Kind = WeaponKind.Melee, AttackRange = settings.AttackRangeMelee };
            }

            if (settings.AllowExplosives)
            {
                // Only check max range, no minimum
                if (HasRocketLauncher(npc) && distance <= RocketMaxRange)
                    return new WeaponChoice { Kind = WeaponKind.RocketLauncher, AttackRange = RocketMaxRange };

                if (HasGrenadeLauncher(npc) && distance <= GrenadeLauncherMaxRange)
                    return new WeaponChoice { Kind = WeaponKind.GrenadeLauncher, AttackRange = GrenadeLauncherMaxRange };
            }

            if (HasFlameThrower(npc))
                return new WeaponChoice { Kind = WeaponKind.FlameThrower, AttackRange = GetFlameThrowerRange(npc) };

            if (HasStandardProjectile(npc))
                return new WeaponChoice { Kind = WeaponKind.BaseProjectile, AttackRange = settings.AttackRangeRanged };

            // 🧨 SATCHEL CYCLE: Only use satchels if in throwing phase or not in cycle yet
            if (settings.AllowExplosives && HasThrownExplosive(npc))
            {
                Item explosive = FindThrownExplosive(npc);
                bool isSatchel = explosive?.info?.shortname?.ToLower() == "explosive.satchel";
                
                if (isSatchel && state != null)
                {
                    // Only use satchels if in throwing phase, waiting for explosion, retreating, or cycle not started
                    if (state.SatchelPhase == WeaponState.SatchelCyclePhase.None ||
                        state.SatchelPhase == WeaponState.SatchelCyclePhase.Throwing ||
                        state.SatchelPhase == WeaponState.SatchelCyclePhase.WaitExplosion ||
                        state.SatchelPhase == WeaponState.SatchelCyclePhase.Retreat)
                    {
                        return new WeaponChoice { Kind = WeaponKind.ThrownExplosive, AttackRange = settings.AttackRangeMelee };
                    }
                }
                else if (!isSatchel)
                {
                    // C4 or other explosives - always allow
                    return new WeaponChoice { Kind = WeaponKind.ThrownExplosive, AttackRange = settings.AttackRangeMelee };
                }
            }

            return new WeaponChoice { Kind = WeaponKind.Melee, AttackRange = settings.AttackRangeMelee };
        }

        private static bool ExecuteRaidWeapon(ScientistNPC npc, BaseCombatEntity target, RaidSettings settings, WeaponChoice choice, float distance)
        {
            ulong netId = npc.net?.ID.Value ?? 0;
            var state = GetState(netId);
            if (state == null) return false;

            // 🧨 SATCHEL CYCLE: Handle satchel cycle state machine (only for satchels)
            // This manages movement and phase transitions, but allows weapon execution when appropriate
            if (choice.Kind == WeaponKind.ThrownExplosive && HasThrownExplosive(npc))
            {
                Item explosive = FindThrownExplosive(npc);
                bool isSatchel = explosive?.info?.shortname?.ToLower() == "explosive.satchel";
                
                if (isSatchel)
                {
                    // Handle satchel cycle - this manages movement and phase transitions
                    // Returns true if cycle is managing behavior (prevents other weapon execution)
                    // Returns false to allow normal weapon execution (throwing or melee)
                    bool cycleBlocking = HandleSatchelCycle(npc, target, state, settings, distance);
                    
                    if (cycleBlocking)
                    {
                        // Cycle is managing behavior (moving, waiting, retreating) - don't execute weapons
                        return true;
                    }
                    // Otherwise, fall through to normal weapon execution below
                }
            }

            bool handled;
            switch (choice.Kind)
            {
                case WeaponKind.RocketLauncher:
                    handled = FireRocketLauncher(npc, target, distance);
                    break;
                case WeaponKind.GrenadeLauncher:
                    handled = FireGrenadeLauncher(npc, target, distance);
                    break;
                case WeaponKind.FlameThrower:
                    handled = FireFlameThrower(npc, target, distance);
                    break;
                case WeaponKind.BaseProjectile:
                    handled = FireBaseProjectile(npc, target, distance, settings.FallbackDamageRanged);
                    break;
                case WeaponKind.ThrownExplosive:
                    handled = UseThrownExplosive(npc, target);
                    break;
                case WeaponKind.Melee:
                    handled = UseMeleeWeapon(npc, target, settings.FallbackDamageMelee);
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled)
                return true;

            // Fallback to standard ranged or melee if special weapon cannot fire.
            if (HasStandardProjectile(npc))
                return FireBaseProjectile(npc, target, distance, settings.FallbackDamageRanged);

            return UseMeleeWeapon(npc, target, settings.FallbackDamageMelee);
        }

        private static bool FireRocketLauncher(ScientistNPC npc, BaseCombatEntity target, float distance)
        {
            if (npc == null || target == null) return false;

            // Only check max range (< 30m), no minimum range check
            // This allows NPCs to fire rockets at any distance up to 30m
            if (distance > RocketMaxRange)
            {
                if (LogWeapons && Time.frameCount % 120 == 0)
                    UnityEngine.Debug.Log($"[GrimmNPC Rocket] NPC {npc.net?.ID.Value ?? 0} too far for rocket (distance={distance:F1}m > {RocketMaxRange}m)");
                return false; // Too far - will fallback to standard weapons
            }

            var state = GetState(npc.net?.ID.Value ?? 0);
            if (state == null) return false;
            
            // Only prevent if already firing (no other cooldowns - user wants it to fire!)
            if (state.IsFiringRocket)
            {
                if (LogWeapons && Time.frameCount % 120 == 0)
                    UnityEngine.Debug.Log($"[GrimmNPC Rocket] NPC {npc.net?.ID.Value ?? 0} already firing rocket");
                return false;
            }

            Item rocketLauncher = FindRocketLauncher(npc);
            if (rocketLauncher == null)
            {
                if (LogWeapons && Time.frameCount % 120 == 0)
                    UnityEngine.Debug.LogWarning($"[GrimmNPC Rocket] NPC {npc.net?.ID.Value ?? 0} rocket launcher not found in belt");
                return false;
            }

            if (LogWeapons)
                UnityEngine.Debug.Log($"[GrimmNPC Rocket] NPC {npc.net?.ID.Value ?? 0} starting rocket fire sequence (distance={distance:F1}m)");

            Item currentItem = npc.GetActiveItem();
            state.PreviousActiveItemUid = currentItem != null ? currentItem.uid : default(ItemId);
            EquipItem(npc, rocketLauncher);

            state.IsFiringRocket = true;
            state.RocketCoroutine = ServerMgr.Instance.StartCoroutine(ProcessFireRocketLauncher(npc, target, state));
            return true;
        }

        internal static IEnumerator ProcessFireRocketLauncher(ScientistNPC npc, BaseCombatEntity target, WeaponState state)
        {
            // Set flag (like NpcSpawn line 1099)
            state.IsFireRocketLauncher = true;
            
            // Equip rocket launcher (like NpcSpawn line 1100)
            Item rocketLauncher = FindRocketLauncher(npc);
            if (rocketLauncher != null)
            {
                EquipItem(npc, rocketLauncher);
            }
            
            var brain = npc.Brain;
            if (brain != null)
            {
                StopNavigator(npc, brain, nameof(ProcessFireRocketLauncher));
                if (target != null && !target.IsDestroyed)
                    brain.Navigator.SetFacingDirectionEntity(target);
            }

            if (!npc.IsMounted())
                npc.SetDucked(true);

            // waits 1.5 seconds before firing (like NpcSpawn line 1104)
            yield return new WaitForSeconds(RocketPreFireDelay);

            // Check target still valid (like NpcSpawn line 1105)
            if (target == null || target.IsDestroyed)
            {
                state.IsFireRocketLauncher = false;
                RestorePreviousItem(npc, state);
                if (brain != null)
                    brain.Navigator.ClearFacingDirectionOverride();
                if (!npc.IsMounted())
                    npc.SetDucked(false);
                yield break;
            }

            // Adjust aim for foundations, then fire (like NpcSpawn lines 1107-1112)
            // CRITICAL: Clear facing direction override BEFORE setting aim (allows NPC to turn)
            if (brain != null)
                brain.Navigator.ClearFacingDirectionOverride();
            
            if (target.ShortPrefabName != null && target.ShortPrefabName.Contains("foundation"))
            {
                Vector3 aimPoint = target.transform.position - new Vector3(0f, 1.5f, 0f);
                Vector3 aimDirection = (aimPoint - npc.transform.position).normalized;
                SetAim(npc, aimDirection);
            }
            else
            {
                // Just aim at target (use CenterPoint for better accuracy)
                Vector3 targetPos = target.CenterPoint();
                Vector3 aimDirection = (targetPos - npc.eyes.position).normalized;
                SetAim(npc, aimDirection);
            }

            // Fire the rocket (like NpcSpawn line 1112)
            FireRocketLauncher(npc);
            
            // Set reload flag and invoke finish (like NpcSpawn lines 1113-1114)
            state.IsReloadRocketLauncher = true;
            npc.Invoke(() => FinishReloadRocketLauncher(npc.net?.ID.Value ?? 0), 6f);

            // Clear flag (like NpcSpawn line 1116)
            state.IsFireRocketLauncher = false;

            // CRITICAL: Match NpcSpawn order exactly (lines 1116-1119):
            // 1. EquipWeapon() - trigger weapon selection FIRST
            // 2. ClearFacingDirectionOverride()
            // 3. SetDucked(false)
            // This ensures NPC can move and turn properly after weapon selection
            BaseEntity currentTarget = null;
            if (brain != null && brain.Events != null && brain.Events.Memory != null)
            {
                int slot = brain.Events.CurrentInputMemorySlot;
                if (slot >= 0)
                    currentTarget = brain.Events.Memory.Entity.Get(slot);
            }
            
            if (currentTarget != null)
            {
                // Trigger weapon selection based on current distance (like NpcSpawn EquipWeapon)
                float currentDistance = Vector3.Distance(npc.transform.position, currentTarget.transform.position);
                Item selectedWeapon = SelectWeaponByDistance(npc, currentDistance, 1f);
                if (selectedWeapon != null)
                {
                    EquipItem(npc, selectedWeapon);
                }
                else
                {
                    // Fallback: restore previous item if weapon selection fails
                    RestorePreviousItem(npc, state);
                }
            }
            else
            {
                // No target - restore previous item
                RestorePreviousItem(npc, state);
            }

            // THEN clear facing direction and set ducked false (like NpcSpawn lines 1118-1119)
            // CRITICAL: Ensure facing direction is cleared so NPC can turn to face target
            if (brain != null)
            {
                brain.Navigator.ClearFacingDirectionOverride();
                // Navigator.Stop() was called at start of coroutine, but navigator will resume
                // when combat movement logic runs or when a new destination is set
                
                // Ensure NPC can turn to face target after weapon swap
                if (currentTarget != null && !currentTarget.IsDestroyed)
                {
                    // Set facing direction to target so NPC turns to face it
                    brain.Navigator.SetFacingDirectionEntity(currentTarget);
                }
            }

            if (!npc.IsMounted())
                npc.SetDucked(false);
        }

        /// <summary>
        /// Throws C4 at target (like NpcSpawn line 1150).
        /// </summary>
        internal static IEnumerator ThrownC4(ScientistNPC npc, BaseCombatEntity target, WeaponState state)
        {
            if (npc == null || target == null || state == null) yield break;
            
            // Find C4 item (like NpcSpawn line 1152)
            Item c4Item = null;
            if (npc.inventory != null && npc.inventory.containerBelt != null)
            {
                foreach (var item in npc.inventory.containerBelt.itemList)
                {
                    if (item?.info?.shortname == "explosive.timed")
                    {
                        c4Item = item;
                        break;
                    }
                }
            }
            
            if (c4Item == null) yield break;
            
            // Set flag (like NpcSpawn line 1153)
            state.IsFireC4 = true;
            
            var brain = npc.Brain;
            if (brain != null && brain.Navigator != null)
            {
                StopNavigator(npc, brain, "ProcessThrowC4");
                brain.Navigator.SetFacingDirectionEntity(target);
            }
            
            // Wait 1.5 seconds (like NpcSpawn line 1156)
            yield return new WaitForSeconds(1.5f);
            
            // Check target still valid (like NpcSpawn line 1157)
            if (target == null || target.IsDestroyed)
            {
                state.IsFireC4 = false;
                if (brain != null && brain.Navigator != null)
                    brain.Navigator.ClearFacingDirectionOverride();
                yield break;
            }
            
            // Throw C4 (like NpcSpawn lines 1159-1160)
            ThrownWeapon weapon = c4Item.GetHeldEntity() as ThrownWeapon;
            if (weapon != null)
            {
                weapon.ServerThrow(target.transform.position);
            }
            
            // Set reload flag and invoke finish (like NpcSpawn lines 1161-1162)
            state.IsReloadC4 = true;
            npc.Invoke(() => FinishReloadC4(npc.net?.ID.Value ?? 0), 15f);
            
            // Clear flag (like NpcSpawn line 1164)
            state.IsFireC4 = false;
            
            if (brain != null && brain.Navigator != null)
                brain.Navigator.ClearFacingDirectionOverride();
        }

        /// <summary>
        /// Throws smoke grenade (like NpcSpawn line 908).
        /// </summary>
        public static void ThrownSmoke(ScientistNPC npc)
        {
            if (npc == null || npc.inventory == null || npc.inventory.containerBelt == null) return;
            
            // Find smoke grenade
            Item smokeItem = null;
            foreach (var item in npc.inventory.containerBelt.itemList)
            {
                if (item?.info?.shortname == "grenade.smoke")
                {
                    smokeItem = item;
                    break;
                }
            }
            
            if (smokeItem == null) return;
            
            GrenadeWeapon weapon = smokeItem.GetHeldEntity() as GrenadeWeapon;
            if (weapon == null) return;
            
            // Throw smoke at current position (like NpcSpawn line 915)
            weapon.ServerThrow(npc.transform.position);
        }

        /// <summary>
        /// Finishes rocket launcher reload (like NpcSpawn line 1144).
        /// </summary>
        private static void FinishReloadRocketLauncher(ulong netId)
        {
            var state = GetState(netId);
            if (state != null)
            {
                state.IsReloadRocketLauncher = false;
            }
        }

        /// <summary>
        /// Finishes C4 reload (like NpcSpawn line 1168).
        /// </summary>
        private static void FinishReloadC4(ulong netId)
        {
            var state = GetState(netId);
            if (state != null)
            {
                state.IsReloadC4 = false;
            }
        }

        /// <summary>
        /// Gets aim direction from current target using HumanNPC's targeting system.
        /// Uses ModifyAIAim if available for better accuracy.
        /// </summary>
        private static Vector3 GetAim(ScientistNPC npc, BaseEntity target)
        {
            if (npc == null || npc.eyes == null)
                return Vector3.forward;

            if (target == null || target.IsDestroyed)
                return npc.eyes.BodyForward();

            float distance = Vector3.Distance(npc.transform.position, target.transform.position);
            if (distance < 2f)
                return npc.eyes.BodyForward();

            Vector3 aimDirection = (target.transform.position - npc.transform.position).normalized;

            // Use HumanNPC's ModifyAIAim if available (like HumanNPC.SetAimDirection does)
            AttackEntity attackEntity = npc.GetAttackEntity();
            if (attackEntity != null)
            {
                // Get aim sway scalar (like HumanNPC.GetAimSwayScalar)
                float aimSwayScalar = 1f;
                try
                {
                    var getAimSwayScalarMethod = npc.GetType().GetMethod("GetAimSwayScalar",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (getAimSwayScalarMethod != null)
                    {
                        aimSwayScalar = (float)getAimSwayScalarMethod.Invoke(npc, null);
                    }
                }
                catch { }

                aimDirection = attackEntity.ModifyAIAim(aimDirection, aimSwayScalar);
            }

            return aimDirection;
        }

        /// <summary>
        /// Checks if target is in engagement range using HumanNPC's EngagementRange method.
        /// </summary>
        private static bool IsTargetInEngagementRange(ScientistNPC npc, BaseEntity target, out float distance)
        {
            distance = 0f;
            if (npc == null || target == null)
                return false;

            distance = Vector3.Distance(npc.transform.position, target.transform.position);

            // Use HumanNPC's IsTargetInRange if available (implements IAIAttack)
            try
            {
                var isTargetInRangeMethod = npc.GetType().GetMethod("IsTargetInRange",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (isTargetInRangeMethod != null)
                {
                    object[] parameters = new object[] { target, null };
                    bool result = (bool)isTargetInRangeMethod.Invoke(npc, parameters);
                    if (parameters[1] != null)
                        distance = (float)parameters[1];
                    return result;
                }
            }
            catch { }

            // Fallback: use EngagementRange method
            float engagementRange = GetEngagementRange(npc);
            return distance <= engagementRange;
        }

        /// <summary>
        /// Gets engagement range using HumanNPC's EngagementRange method.
        /// </summary>
        private static float GetEngagementRange(ScientistNPC npc)
        {
            if (npc == null)
                return 50f;

            // Use HumanNPC's EngagementRange if available
            try
            {
                var engagementRangeMethod = npc.GetType().GetMethod("EngagementRange",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (engagementRangeMethod != null)
                {
                    return (float)engagementRangeMethod.Invoke(npc, null);
                }
            }
            catch { }

            // Fallback: calculate from attack entity
            AttackEntity attackEntity = npc.GetAttackEntity();
            if (attackEntity != null)
            {
                var brain = npc.Brain;
                float multiplier = brain != null ? brain.AttackRangeMultiplier : 1f;
                return attackEntity.effectiveRange * (attackEntity.aiOnlyInRange ? 1f : 2f) * multiplier;
            }

            // Final fallback
            var brain2 = npc.Brain;
            return brain2 != null ? brain2.SenseRange : 50f;
        }

        /// <summary>
        /// Fires rocket launcher using simple approach: just create and fire the rocket directly.
        /// No ammo checks, no reload logic - just shoot the rocket directly.
        /// </summary>
        private static bool FireRocketLauncher(ScientistNPC npc)
        {
            if (npc == null || npc.eyes == null) return false;

            // Just signal attack and create rocket directly
            npc.SignalBroadcast(BaseEntity.Signal.Attack, string.Empty, null);
            
            // Get spawn position 
            Vector3 spawnOrigin = npc.IsMounted() ? npc.eyes.position + new Vector3(0f, 0.5f, 0f) : npc.eyes.position;
            
            // Calculate aim direction
            Vector3 modifiedAimConeDirection = AimConeUtil.GetModifiedAimConeDirection(2.25f, npc.eyes.BodyForward());
            
            // Check for obstacles 
            float spawnDistance = 1f;
            RaycastHit hit;
            if (Physics.Raycast(spawnOrigin, modifiedAimConeDirection, out hit, spawnDistance, 1236478737))
            {
                spawnDistance = hit.distance - 0.1f;
            }
            
            // Create rocket directly (uses hardcoded prefab path)
            TimedExplosive rocket = GameManager.server.CreateEntity(
                "assets/prefabs/ammo/rocket/rocket_basic.prefab",
                spawnOrigin + modifiedAimConeDirection * spawnDistance
            ) as TimedExplosive;
            
            if (rocket == null)
            {
                UnityEngine.Debug.LogError($"[GrimmNPC Rocket] NPC {npc.net?.ID.Value ?? 0} failed to create rocket entity");
                return false;
            }
            
            rocket.creatorEntity = npc;
            
            // Initialize velocity 
            ServerProjectile serverProjectile = rocket.GetComponent<ServerProjectile>();
            if (serverProjectile != null)
            {
                Vector3 inheritedVelocity = Vector3.zero;
                try
                {
                    var getInheritedMethod = npc.GetType().GetMethod("GetInheritedProjectileVelocity",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (getInheritedMethod != null)
                    {
                        inheritedVelocity = (Vector3)getInheritedMethod.Invoke(npc, new object[] { modifiedAimConeDirection });
                    }
                }
                catch
                {
                    inheritedVelocity = Vector3.zero;
                }
                
                // Use 2f speed multiplier
                Vector3 finalVelocity = inheritedVelocity + modifiedAimConeDirection * serverProjectile.speed * 2f;
                serverProjectile.InitializeVelocity(finalVelocity);
            }
            
            // Spawn the rocket
            rocket.Spawn();
            
            if (LogWeapons)
                UnityEngine.Debug.Log($"[GrimmNPC Rocket] NPC {npc.net?.ID.Value ?? 0} fired rocket ");
            
            return true;
        }

        /// <summary>
        /// Calculates rocket offset for better accuracy (like BotReSpawn.Offset).
        /// </summary>
        private static Vector3 CalculateRocketOffset(string resourcePath, float distance)
        {
            if (string.IsNullOrEmpty(resourcePath))
                return Vector3.zero;

            // BotReSpawn uses different offsets based on projectile type
            if (resourcePath.Contains("_he"))
                return new Vector3(0, distance * (distance / 10) / 18f, 0);
            if (resourcePath.Contains("_hv"))
                return new Vector3(0, distance * (distance / 350) / 18f, 0);
            
            // Default rocket offset
            return new Vector3(0, distance * (distance / 13) / 18f, 0);
        }

        private static bool FireGrenadeLauncher(ScientistNPC npc, BaseCombatEntity target, float distance)
        {
            if (npc == null || target == null) return false;
            // Only check max range, no minimum
            if (distance > GrenadeLauncherMaxRange) return false;

            var state = GetState(npc.net?.ID.Value ?? 0);
            if (state == null) return false;

            if (Time.time < state.GrenadeLauncherReloadUntil || Time.time < state.NextGrenadeLauncherFireTime)
                return false;

            Item grenadeLauncher = FindGrenadeLauncher(npc);
            if (grenadeLauncher == null) return false;

            EquipItem(npc, grenadeLauncher);
            SyncGrenadeLauncherAmmo(grenadeLauncher, state);

            state.NextGrenadeLauncherFireTime = Time.time + GrenadeLauncherFireInterval;
            state.GrenadeLauncherCoroutine = ServerMgr.Instance.StartCoroutine(ProcessFireGrenadeLauncher(npc, target, state));
            return true;
        }

        private static IEnumerator ProcessFireGrenadeLauncher(ScientistNPC npc, BaseCombatEntity target, WeaponState state)
        {
            var brain = npc.Brain;
            if (brain != null)
            {
                StopNavigator(npc, brain, nameof(ProcessFireGrenadeLauncher));
                brain.Navigator.SetFacingDirectionEntity(target);
            }

            yield return new WaitForSeconds(GrenadeLauncherPreFireDelay);

            if (target != null && !target.IsDestroyed && HasLineOfSight(npc, target))
            {
                SetAim(npc, (target.transform.position - npc.transform.position).normalized);
                FireGrenadeLauncher(npc, state);
            }

            if (brain != null)
                brain.Navigator.ClearFacingDirectionOverride();
        }

        /// <summary>
        /// Fires grenade launcher using simple approach.
        /// </summary>
        private static void FireGrenadeLauncher(ScientistNPC npc, WeaponState state)
        {
            if (npc == null || npc.eyes == null || state == null) return;

            // Signal attack, create grenade directly
            npc.SignalBroadcast(BaseEntity.Signal.Attack, string.Empty);
            Vector3 origin = npc.IsMounted() ? npc.eyes.position + new Vector3(0f, 0.5f, 0f) : npc.eyes.position;
            Vector3 direction = AimConeUtil.GetModifiedAimConeDirection(0.675f, npc.eyes.BodyForward());

            float spawnDistance = 1f;
            RaycastHit hit;
            if (Physics.Raycast(origin, direction, out hit, spawnDistance, 1236478737))
                spawnDistance = hit.distance - 0.1f;

            // Use ammo type from state (default to HE)
            string ammoType = string.IsNullOrEmpty(state.GrenadeLauncherAmmoType)
                ? "40mm_grenade_he"
                : state.GrenadeLauncherAmmoType;

            TimedExplosive grenade = GameManager.server.CreateEntity(
                $"assets/prefabs/ammo/40mmgrenade/{ammoType}.prefab",
                origin + direction * spawnDistance) as TimedExplosive;

            if (grenade == null) return;

            grenade.creatorEntity = npc;
            ServerProjectile serverProjectile = grenade.GetComponent<ServerProjectile>();
            if (serverProjectile != null)
            {
                serverProjectile.InitializeVelocity(npc.GetInheritedProjectileVelocity(direction) + direction * serverProjectile.speed * 2f);
            }
            grenade.Spawn();

            // Track ammo count, reload after 6 shots
            state.GrenadeLauncherAmmoRemaining--;
            if (state.GrenadeLauncherAmmoRemaining <= 0)
            {
                state.GrenadeLauncherAmmoRemaining = GrenadeLauncherMagazineSize;
                state.GrenadeLauncherReloadUntil = Time.time + GrenadeLauncherReloadDuration;
            }
        }

        /// <summary>
        /// Tries to fire flamethrower at target. Holds trigger continuously while in range (like NpcSpawn CombatState).
        /// CRITICAL: Flamethrower should be equipped via weapon selection first, then we just fire it when in range.
        /// The flame stays on continuously while in range and has ammo, and turns off when out of range or ammo runs out.
        /// </summary>
        private static bool FireFlameThrower(ScientistNPC npc, BaseCombatEntity target, float distance)
        {
            if (npc == null || target == null) return false;

            var state = GetState(npc.net?.ID.Value ?? 0);
            if (state == null) return false;

            // CRITICAL: Check effectiveRange first (before getting weapon) to determine if we should use flamethrower
            Item flameThrowerItem = FindFlameThrower(npc);
            if (flameThrowerItem == null) return false;
            
            // Get effective range from the flamethrower item
            FlameThrower flameThrowerEntity = flameThrowerItem.GetHeldEntity() as FlameThrower;
            float effectiveRange = flameThrowerEntity != null ? flameThrowerEntity.effectiveRange : 5f;
            if (effectiveRange <= 0f) effectiveRange = 5f; // Default if not set

            // CRITICAL: If in range, ensure flamethrower is equipped (fixes swap issue)
            if (distance <= effectiveRange)
            {
                // In range - ensure flamethrower is equipped
                Item currentWeapon = npc.GetActiveItem();
                if (currentWeapon == null || currentWeapon != flameThrowerItem)
                {
                    // Not equipped - equip it now
                    EquipItem(npc, flameThrowerItem);
                    // Small delay to allow weapon to equip
                    return true; // Return true to indicate we're handling it, will fire next frame
                }
            }

            // Get flamethrower from current weapon (should be equipped now)
            FlameThrower flameThrower = npc.GetHeldEntity() as FlameThrower;
            if (flameThrower == null)
            {
                // Still not equipped - try again
                EquipItem(npc, flameThrowerItem);
                flameThrower = npc.GetHeldEntity() as FlameThrower;
                if (flameThrower == null) return false;
            }
            
            var brain = npc.Brain;
            
            // Check if out of range - turn off flame if active
            if (distance > effectiveRange)
            {
                // Too far - turn off flame if it's on
                if (state.IsFlameThrowerActive && flameThrower.IsFlameOn())
                {
                    flameThrower.SetFlameState(false);
                    state.IsFlameThrowerActive = false;
                    state.FlameThrowerSwappedWeapon = false; // Reset swap flag
                }
                
                // User wants: if player out of range, use ranged weapons
                // Swap to ranged weapon if we're still using flamethrower
                Item currentWeapon = npc.GetActiveItem();
                if (currentWeapon != null && (currentWeapon.info?.shortname == "flamethrower" || currentWeapon.info?.shortname == "militaryflamethrower"))
                {
                    // Swap to ranged weapon
                    Item selectedWeapon = SelectWeaponByDistance(npc, distance, 1f);
                    if (selectedWeapon != null && selectedWeapon != currentWeapon)
                    {
                        EquipItem(npc, selectedWeapon);
                    }
                }
                
                // Push forward toward target (full speed) while shooting ranged weapon
                if (brain != null && brain.Navigator != null)
                {
                    Vector3 targetPos = target.transform.position;
                    Vector3 destination = GrimmNPC.GetCombatApproachPosition(npc, targetPos, 2f);
                    brain.Navigator.SetDestination(destination, BaseNavigator.NavigationSpeed.Fast); // Full speed to close gap
                }
                return false; // Not in range yet
            }

            // In range - check ammo and reload if needed (like NpcSpawn FireFlameThrower lines 1305-1310)
            if (flameThrower.ammo <= 0)
            {
                // Out of ammo - turn off flame and start reload
                if (state.IsFlameThrowerActive && flameThrower.IsFlameOn())
                {
                    flameThrower.SetFlameState(false);
                    state.IsFlameThrowerActive = false;
                }
                
                // User wants: NO back away - keep pushing forward while reloading
                // Swap to ranged weapon while reloading (user wants: swap weapons, shoot gun while pushing)
                if (!state.FlameThrowerSwappedWeapon)
                {
                    // Trigger weapon selection to get a ranged weapon
                    float reloadDistance = distance;
                    Item selectedWeapon = SelectWeaponByDistance(npc, reloadDistance, 1f);
                    if (selectedWeapon != null)
                    {
                        EquipItem(npc, selectedWeapon);
                        state.FlameThrowerSwappedWeapon = true;
                    }
                }
                
                // Keep pushing forward while reloading (no back away)
                if (brain != null && brain.Navigator != null)
                {
                    Vector3 targetPos = target.transform.position;
                    Vector3 npcPos = npc.transform.position;
                    Vector3 pushPosition = GrimmNPC.GetCombatApproachPosition(npc, npcPos, targetPos, 2f);
                    brain.Navigator.SetDestination(pushPosition, BaseNavigator.NavigationSpeed.Fast);
                }
                
                if (state.FlameThrowerReloadUntil == 0f)
                {
                    state.FlameThrowerReloadUntil = Time.time + FlameThrowerReloadDuration;
                }
                else if (Time.time >= state.FlameThrowerReloadUntil)
                {
                    // Reload complete - top up ammo (like NpcSpawn FinishReloadFlameThrower line 1319)
                    flameThrower.TopUpAmmo();
                    state.FlameThrowerReloadUntil = 0f;
                    state.FlameThrowerSwappedWeapon = false; // Reset swap flag
                    
                    // If still in range, swap back to flamethrower (user wants: swap back if still in range)
                    if (distance <= effectiveRange)
                    {
                        Item flameItem = FindFlameThrower(npc);
                        if (flameItem != null)
                        {
                            EquipItem(npc, flameItem);
                        }
                    }
                }
                return false; // Still reloading or just started reload
            }

            // In range and has ammo - manage movement and fire continuously
            // User wants: Push forward while firing, never get closer than 2f, keep pushing if player backs away
            string shortName = flameThrower.ShortPrefabName ?? string.Empty;
            
            // Calculate minimum distance (2f as requested - never get closer)
            float minDistance = 2f;
            
            // Fire continuously while in range (hold trigger down)
            if (!state.IsFlameThrowerActive || !flameThrower.IsFlameOn())
            {
                flameThrower.SetFlameState(true);
                state.IsFlameThrowerActive = true;
            }
            
            // Movement logic: Push forward while firing, but never get closer than 2f
            // If player backs away, keep pushing while firing
            if (brain != null && brain.Navigator != null)
            {
                Vector3 targetPos = target.transform.position;
                Vector3 npcPos = npc.transform.position;
                Vector3 toTarget = (targetPos - npcPos).normalized;
                
                // Calculate current distance
                float currentDist = distance;
                
                // If we're too close (less than 2f), back away slightly
                if (currentDist < minDistance)
                {
                    // Back away to maintain 2f minimum
                    Vector3 retreatPos = npcPos - toTarget * (minDistance - currentDist + 0.5f);
                    brain.Navigator.SetDestination(retreatPos, BaseNavigator.NavigationSpeed.Normal);
                }
                else
                {
                    // Push forward toward target (full speed) while firing
                    // Target position at minimum distance (2f) from target
                    Vector3 pushPosition = GrimmNPC.GetCombatApproachPosition(npc, npcPos, targetPos, minDistance);
                    brain.Navigator.SetDestination(pushPosition, BaseNavigator.NavigationSpeed.Fast);
                }
            }

            return true;
        }

        private static bool FireBaseProjectile(ScientistNPC npc, BaseCombatEntity target, float distance, float fallbackDamage)
        {
            if (npc == null || target == null) return false;

            // Find and equip standard projectile weapon if not already equipped
            Item activeItem = npc.GetActiveItem();
            AttackEntity activeAttack = npc.GetAttackEntity();
            
            // Check if current weapon is a standard projectile
            if (!(activeAttack is BaseProjectile) || IsSpecialRangedWeapon(activeItem?.info?.shortname ?? string.Empty))
            {
                // Need to equip standard weapon
                Item standardWeapon = FindBeltItem(npc, item =>
                {
                    if (item == null || item.info == null) return false;
                    string shortName = item.info.shortname ?? string.Empty;
                    AttackEntity attackEntity = item.GetHeldEntity() as AttackEntity;
                    return attackEntity is BaseProjectile && !IsSpecialRangedWeapon(shortName);
                });
                
                if (standardWeapon != null)
                {
                    EquipItem(npc, standardWeapon);
                    activeItem = standardWeapon;
                    activeAttack = npc.GetAttackEntity();
                }
                else
                {
                    if (LogWeapons && Time.frameCount % 120 == 0)
                        UnityEngine.Debug.LogWarning($"[GrimmNPC BaseProjectile] NPC {npc.net?.ID.Value ?? 0} no standard projectile weapon found");
                    return false;
                }
            }

            string shortName = activeItem?.info?.shortname ?? string.Empty;
            if (IsBowWeapon(shortName))
                return FireBow(npc, target, distance, fallbackDamage, shortName);

            // Use HumanNPC's improved targeting system (includes ModifyAIAim)
            Vector3 aimDirection = GetAim(npc, target);
            SetAim(npc, aimDirection);
            
            // Use HumanNPC's ShotTest which handles all BaseProjectile weapons (including blowpipe, crossbow, flint strike, etc.)
            bool shotResult = npc.ShotTest(distance);
            if (shotResult)
            {
                if (LogWeapons && Time.frameCount % 120 == 0)
                    UnityEngine.Debug.Log($"[GrimmNPC BaseProjectile] NPC {npc.net?.ID.Value ?? 0} fired {shortName} via ShotTest (distance={distance:F1}m)");
                return true;
            }

            // Fallback: direct damage if ShotTest fails
            target.OnAttacked(fallbackDamage, DamageType.Explosion, npc, false);
            if (LogWeapons && Time.frameCount % 120 == 0)
                UnityEngine.Debug.Log($"[GrimmNPC BaseProjectile] NPC {npc.net?.ID.Value ?? 0} ShotTest failed, using fallback damage (distance={distance:F1}m)");
            return true;
        }

        private static bool FireBow(ScientistNPC npc, BaseCombatEntity target, float distance, float fallbackDamage, string shortName)
        {
            var state = GetState(npc.net?.ID.Value ?? 0);
            if (state == null) return false;

            if (state.IsDrawingBow || Time.time < state.NextBowFireTime)
                return true;

            state.IsDrawingBow = true;
            state.NextBowFireTime = Time.time + GetBowDrawDelay(shortName);
            state.BowCoroutine = ServerMgr.Instance.StartCoroutine(ProcessFireBow(npc, target, distance, fallbackDamage, state, shortName));
            return true;
        }

        private static IEnumerator ProcessFireBow(ScientistNPC npc, BaseCombatEntity target, float distance, float fallbackDamage, WeaponState state, string shortName)
        {
            var brain = npc.Brain;
            if (brain != null)
            {
                StopNavigator(npc, brain, nameof(ProcessFireBow));
                brain.Navigator.SetFacingDirectionEntity(target);
            }

            yield return new WaitForSeconds(GetBowDrawDelay(shortName));

            // Use HumanNPC's CanSeeTarget for LOS check
            bool hasLOS = false;
            try
            {
                var canSeeTargetMethod = npc.GetType().GetMethod("CanSeeTarget",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (canSeeTargetMethod != null)
                {
                    hasLOS = (bool)canSeeTargetMethod.Invoke(npc, new object[] { target });
                }
                else
                {
                    hasLOS = HasLineOfSight(npc, target);
                }
            }
            catch
            {
                hasLOS = HasLineOfSight(npc, target);
            }

            if (target != null && !target.IsDestroyed && hasLOS)
            {
                // Use HumanNPC's improved targeting system (includes ModifyAIAim)
                Vector3 aimDirection = GetAim(npc, target);
                SetAim(npc, aimDirection);
                
                // ShotTest handles all BaseProjectile weapons (bow, crossbow, blowpipe, etc.)
                if (!npc.ShotTest(distance))
                {
                    target.OnAttacked(fallbackDamage, DamageType.Explosion, npc, false);
                }
            }

            state.IsDrawingBow = false;

            if (brain != null)
                brain.Navigator.ClearFacingDirectionOverride();
        }

        /// <summary>
        /// 🧨 SATCHEL CYCLE STATE MACHINE: Manages the cycle of throw → wait → melee → retreat → repeat
        /// Based on RaidingZombies pattern: check for wall/structure, throw if in range, otherwise move around
        /// Returns true if cycle is managing behavior (prevents other weapon execution), false to allow normal weapon execution
        /// </summary>
        private static bool HandleSatchelCycle(ScientistNPC npc, BaseCombatEntity target, WeaponState state, RaidSettings settings, float distance)
        {
            if (npc == null || target == null || state == null) return false;

            var brain = npc.Brain;
            if (brain == null || brain.Navigator == null) return false;

            float currentTime = Time.time;
            Vector3 npcPos = npc.transform.position;
            Vector3 targetPos = target.transform.position;

            // Initialize cycle if not started
            if (state.SatchelPhase == WeaponState.SatchelCyclePhase.None)
            {
                state.SatchelPhase = WeaponState.SatchelCyclePhase.Throwing;
                state.SatchelPhaseStartTime = currentTime;
                state.SatchelsThrownThisCycle = 0;
            }

            // Handle phase transitions and movement (like RaidingZombies pattern)
            switch (state.SatchelPhase)
            {
                case WeaponState.SatchelCyclePhase.Throwing:
                    // Throw satchels until we've thrown enough
                    if (state.SatchelsThrownThisCycle < SatchelsPerCycle)
                    {
                        // Check if we have a wall/structure in throwable range (like RaidingZombies.TargetInThrowableRange)
                        bool hasWallInRange = HasWallInThrowableRange(npc, target, 8f);
                        
                        // Check range - need to be in optimal range to throw
                        if (distance >= SatchelMinRange && distance <= SatchelMaxRange && hasWallInRange)
                        {
                            // In range with wall - allow throwing to happen (don't block)
                            return false; // Let UseThrownExplosive execute
                        }
                        else if (distance > SatchelMaxRange || !hasWallInRange)
                        {
                            // Too far or no wall - move around target (like RaidingZombies moves 2-12m around target)
                            // Use PathFinder.GetRandomPositionAround if available, otherwise calculate position
                            Vector3 movePos;
                            if (brain.PathFinder != null)
                            {
                                movePos = brain.PathFinder.GetRandomPositionAround(targetPos, 2f, 12f);
                            }
                            else
                            {
                                // Fallback: move to optimal range
                                movePos = GrimmNPC.GetCombatApproachPosition(npc, npcPos, targetPos, SatchelOptimalRange);
                            }
                            brain.Navigator.SetDestination(movePos, BaseNavigator.NavigationSpeed.Fast);
                            return true; // Block other actions while moving
                        }
                        else if (distance < SatchelMinRange)
                        {
                            // Too close - move away to optimal range
                            Vector3 directionAway = (npcPos - targetPos).normalized;
                            Vector3 movePos = targetPos - directionAway * SatchelOptimalRange;
                            brain.Navigator.SetDestination(movePos, BaseNavigator.NavigationSpeed.Normal);
                            return true; // Block other actions while moving
                        }
                        return false; // Allow throwing
                    }
                    else
                    {
                        // All satchels thrown - transition to waiting for explosion
                        state.SatchelPhase = WeaponState.SatchelCyclePhase.WaitExplosion;
                        state.SatchelPhaseStartTime = currentTime;
                        return true; // Block actions while waiting
                    }

                case WeaponState.SatchelCyclePhase.WaitExplosion:
                    // Wait for satchels to explode (8 seconds) - like RaidingZombies waits after throw
                    if (currentTime - state.SatchelPhaseStartTime < SatchelExplosionWaitTime)
                    {
                        // Stay in place, don't attack yet (like RaidingZombies stops before throwing)
                        if (brain.Navigator.Moving)
                        {
                            StopNavigator(npc, brain, nameof(HandleSatchelCycle));
                        }
                        return true; // Block actions while waiting
                    }
                    else
                    {
                        // Explosion wait complete - transition to melee attack
                        state.SatchelPhase = WeaponState.SatchelCyclePhase.MeleeAttack;
                        state.SatchelPhaseStartTime = currentTime;
                        state.MeleeAttackEndTime = currentTime + MeleeAttackDuration;
                        return false; // Allow melee attack
                    }

                case WeaponState.SatchelCyclePhase.MeleeAttack:
                    // Melee attack for 30 seconds
                    if (currentTime < state.MeleeAttackEndTime)
                    {
                        // Move to melee range and attack
                        if (distance > settings.AttackRangeMelee)
                        {
                            brain.Navigator.SetDestination(GrimmNPC.GetCombatApproachPosition(npc, targetPos, 2f), BaseNavigator.NavigationSpeed.Fast);
                            return true; // Block actions while moving to melee range
                        }
                        else
                        {
                            // In melee range - stop and allow melee attack
                            if (brain.Navigator.Moving)
                            {
                                StopNavigator(npc, brain, nameof(HandleSatchelCycle));
                            }
                            return false; // Allow melee attack to execute
                        }
                    }
                    else
                    {
                        // Melee attack complete - transition to retreat
                        state.SatchelPhase = WeaponState.SatchelCyclePhase.Retreat;
                        state.SatchelPhaseStartTime = currentTime;
                        return true; // Block actions while transitioning
                    }

                case WeaponState.SatchelCyclePhase.Retreat:
                    // Retreat to safe distance (12f) to avoid satchel explosions
                    // Retreat lasts 10 seconds to give satchels time to explode
                    if (currentTime - state.SatchelPhaseStartTime < RetreatDuration)
                    {
                        // Still in retreat phase - move to safe distance
                        if (distance < RetreatDistance)
                        {
                            // Still too close - retreat to safe distance
                            Vector3 directionAway = (npcPos - targetPos).normalized;
                            Vector3 retreatPos = targetPos - directionAway * RetreatDistance;
                            brain.Navigator.SetDestination(retreatPos, BaseNavigator.NavigationSpeed.Normal);
                        }
                        else
                        {
                            // At safe distance - stay there during retreat phase (like RaidingZombies stops)
                            if (brain.Navigator.Moving)
                            {
                                StopNavigator(npc, brain, nameof(HandleSatchelCycle));
                            }
                        }
                        return true; // Block actions while retreating
                    }
                    else
                    {
                        // Retreat complete - reset cycle to throw more satchels
                        state.SatchelPhase = WeaponState.SatchelCyclePhase.Throwing;
                        state.SatchelPhaseStartTime = currentTime;
                        state.SatchelsThrownThisCycle = 0;
                        return false; // Allow throwing to start again
                    }
            }

            return false;
        }

        /// <summary>
        /// 🧨 Check if there's a wall/structure in throwable range (like RaidingZombies.hasWall)
        /// </summary>
        private static bool HasWallInThrowableRange(ScientistNPC npc, BaseCombatEntity target, float maxRange)
        {
            if (npc == null || target == null) return false;
            
            Vector3 origin = (npc.eyes != null) ? npc.eyes.position : npc.transform.position;
            Vector3 dir = (target.transform.position - origin).normalized;
            
            RaycastHit hit;
            int constructionLayer = 1 << 21; // Construction layer (like RaidingZombies.targetLayer)
            
            if (Physics.Raycast(origin, dir, out hit, maxRange, constructionLayer))
            {
                return true; // Found a wall/structure in range
            }
            return false;
        }

        /// <summary>
        /// Uses melee weapon with full NpcSpawn logic: cooldown check, swing effects, special weapon handling (Chainsaw/Jackhammer), and proper damage calculation.
        /// </summary>
        private static bool UseMeleeWeapon(ScientistNPC npc, BaseCombatEntity target, float fallbackDamage)
        {
            if (npc == null || target == null) return false;

            // Get current weapon (should be melee)
            BaseMelee weapon = npc.GetHeldEntity() as BaseMelee;
            if (weapon == null)
            {
                // Try to find and equip melee weapon
                Item meleeItem = FindBeltItem(npc, item =>
                {
                    if (item == null || item.info == null) return false;
                    return item.GetHeldEntity() is BaseMelee;
                });
                if (meleeItem == null)
                {
                    // No melee weapon - use fallback damage
                    target.OnAttacked(new HitInfo(npc, target, DamageType.Generic, fallbackDamage));
                    return true;
                }
                EquipItem(npc, meleeItem);
                weapon = npc.GetHeldEntity() as BaseMelee;
                if (weapon == null) return false;
            }

            // Check attack cooldown (like NpcSpawn line 1328)
            if (weapon.HasAttackCooldown()) return false;

            // Set attack cooldown (like NpcSpawn line 1329)
            weapon.StartAttackCooldown(weapon.repeatDelay * 2f);

            // Signal attack (like NpcSpawn line 1330)
            npc.SignalBroadcast(BaseEntity.Signal.Attack, string.Empty, null);

            // Play swing effect (like NpcSpawn line 1331)
            if (weapon.swingEffect.isValid)
            {
                Effect.server.Run(weapon.swingEffect.resourcePath, weapon.transform.position, Vector3.forward, npc.net?.connection, false);
            }

            // Special handling for Chainsaw (like NpcSpawn lines 1332-1337)
            if (weapon is Chainsaw chainsaw)
            {
                chainsaw.SetAttackStatus(true, BaseEntity.FlagsUpdateMode.SendNetworkUpdate_Flags);
                npc.Invoke(() =>
                {
                    if (chainsaw != null && !chainsaw.IsDestroyed)
                        chainsaw.SetAttackStatus(false, BaseEntity.FlagsUpdateMode.SendNetworkUpdate_Flags);
                }, chainsaw.attackSpacing + 0.5f);
            }

            // Special handling for Jackhammer (like NpcSpawn lines 1338-1343)
            if (weapon is Jackhammer jackhammer)
            {
                jackhammer.SetEngineStatus(true);
                npc.Invoke(() =>
                {
                    if (jackhammer != null && !jackhammer.IsDestroyed)
                        jackhammer.SetEngineStatus(false);
                }, jackhammer.attackSpacing + 0.5f);
            }

            // Damage calculation (like NpcSpawn lines 1344-1374)
            Vector3 forward = npc.eyes.BodyForward();
            for (int i = 0; i < 2; i++)
            {
                List<RaycastHit> hits = Pool.Get<List<RaycastHit>>();
                Vector3 rayOrigin = npc.eyes.position - (forward * (i == 0 ? 0f : 0.2f));
                float radius = i == 0 ? 0f : weapon.attackRadius;
                float maxDistance = weapon.effectiveRange + 0.2f;
                
                GamePhysics.TraceAll(new Ray(rayOrigin, forward), radius, hits, maxDistance, 1219701521);
                
                bool hitSomething = false;
                for (int j = 0; j < hits.Count; j++)
                {
                    RaycastHit hit = hits[j];
                    BaseEntity entity = hit.GetEntity();
                    
                    if (entity != null && entity != npc && !entity.EqualNetID(npc) && !entity.isClient)
                    {
                        // Calculate damage (like NpcSpawn line 1357)
                        float damage = 0f;
                        foreach (var damageType in weapon.damageTypes)
                        {
                            damage += damageType.amount;
                        }
                        
                        // Get NPC damage scale from CustomNpcData (like NpcSpawn Config.DamageScale)
                        float damageScale = 1f;
                        try
                        {
                            ulong netId = npc.net?.ID.Value ?? 0;
                            if (netId != 0)
                            {
                                var npcData = GrimmNPC.GetNpcData(netId);
                                if (npcData != null)
                                    damageScale = npcData.DamageScale;
                            }
                        }
                        catch { }
                        
                        // Apply damage (like NpcSpawn line 1358)
                        entity.OnAttacked(new HitInfo(npc, entity, DamageType.Slash, damage * weapon.npcDamageScale * damageScale));
                        
                        // Get HitInfo from pool (like NpcSpawn line 1359)
                        HitInfo hitInfo = Pool.Get<HitInfo>();
                        hitInfo.HitEntity = entity;
                        hitInfo.HitPositionWorld = hit.point;
                        hitInfo.HitNormalWorld = -forward;
                        
                        // Set hit material (like NpcSpawn lines 1363-1364)
                        if (entity is BaseNpc || entity is BasePlayer)
                            hitInfo.HitMaterial = StringPool.Get("Flesh");
                        else
                        {
                            string materialName = "generic";
                            if (hit.collider != null && hit.collider.sharedMaterial != null)
                                materialName = hit.collider.sharedMaterial.GetName();
                            hitInfo.HitMaterial = StringPool.Get(materialName);
                        }
                        
                        // Call weapon's ServerUse_OnHit (like NpcSpawn line 1365)
                        weapon.ServerUse_OnHit(hitInfo);
                        
                        // Play impact effect (like NpcSpawn line 1366)
                        Effect.server.ImpactEffect(hitInfo);
                        
                        // Free HitInfo back to pool (like NpcSpawn line 1367)
                        Pool.Free(ref hitInfo);
                        
                        hitSomething = true;
                        if (entity.ShouldBlockProjectiles()) break;
                    }
                }
                
                Pool.FreeUnmanaged(ref hits);
                if (hitSomething) break;
            }

            return true;
        }

        /// <summary>
        /// 🧨 SATCHEL THROWING: Uses coroutine pattern like RaidingZombies for proper wind-up, aim stabilization, and preventing spinning.
        /// Entry point that gates re-entrance (isThrowingSatchel) and starts a coroutine.
        /// </summary>
        private static bool UseThrownExplosive(ScientistNPC npc, BaseCombatEntity target)
        {
            if (npc == null || target == null) return false;

            ulong netId = npc.net?.ID.Value ?? 0;
            if (netId == 0) return false;

            var state = GetState(netId);
            if (state == null) return false;

            // Gate re-entrance - prevent multiple throws at once
            if (state.IsThrowingSatchel)
                return true; // Already throwing, wait

            // Check cooldown
            if (Time.time < state.NextSatchelThrowTime)
                return false;

            Item explosive = FindThrownExplosive(npc);
            if (explosive == null) return false;

            bool isSatchel = explosive.info?.shortname?.ToLower() == "explosive.satchel";
            
            Vector3 npcPos = npc.transform.position;
            Vector3 targetPos = target.transform.position;
            float distance = Vector3.Distance(npcPos, targetPos);
            
            // Check if satchel (different handling than C4)
            if (isSatchel)
            {
                // Range checks - approach optimal range if needed
                if (distance < SatchelMinRange)
                {
                    // Too close - move away slightly
                    var brain = npc.Brain;
                    if (brain != null && brain.Navigator != null)
                    {
                        Vector3 directionAway = (npcPos - targetPos).normalized;
                        Vector3 movePos = npcPos + directionAway * SatchelApproachDistance;
                        brain.Navigator.SetDestination(movePos, BaseNavigator.NavigationSpeed.Normal);
                    }
                    return false; // Don't throw yet, need to move away
                }
                else if (distance > SatchelMaxRange)
                {
                    // Too far - approach optimal throw radius
                    var brain = npc.Brain;
                    if (brain != null && brain.Navigator != null)
                    {
                        Vector3 optimalPos = GrimmNPC.GetCombatApproachPosition(npc, npcPos, targetPos, SatchelOptimalRange);
                        brain.Navigator.SetDestination(optimalPos, BaseNavigator.NavigationSpeed.Fast);
                    }
                    return false; // Don't throw yet, need to move closer
                }
                else if (distance > SatchelOptimalRange * 1.2f || distance < SatchelOptimalRange * 0.8f)
                {
                    // Within range but not optimal - approach optimal throw radius
                    var brain = npc.Brain;
                    if (brain != null && brain.Navigator != null)
                    {
                        Vector3 optimalPos = GrimmNPC.GetCombatApproachPosition(npc, npcPos, targetPos, SatchelOptimalRange);
                        brain.Navigator.SetDestination(optimalPos, BaseNavigator.NavigationSpeed.Normal);
                    }
                    // Still try to throw if close enough (allow approach while throwing)
                }
            }

            // Start coroutine for satchel throwing (like RaidingZombies pattern)
            state.IsThrowingSatchel = true;
            state.NextSatchelThrowTime = Time.time + SatchelThrowCooldown;
            state.SatchelCoroutine = ServerMgr.Instance.StartCoroutine(ProcessThrowSatchel(npc, target, explosive, state));
            return true;
        }

        /// <summary>
        /// Throwing coroutine using simple approach for C4, with satchel cycle support for satchels.
        /// </summary>
        private static IEnumerator ProcessThrowSatchel(ScientistNPC npc, BaseCombatEntity target, Item explosive, WeaponState state)
        {
            var brain = npc.Brain;
            if (brain == null || brain.Navigator == null)
            {
                state.IsThrowingSatchel = false;
                state.ThrownWeaponEntity = null;
                yield break;
            }

            bool isSatchel = explosive?.info?.shortname?.ToLower() == "explosive.satchel";

            try
            {
                // Equip, stop, face target, wait 1.5s, throw
                EquipItem(npc, explosive);
                ThrownWeapon thrown = npc.GetActiveItem()?.GetHeldEntity() as ThrownWeapon;
                if (thrown == null)
                {
                    state.IsThrowingSatchel = false;
                    state.ThrownWeaponEntity = null;
                    yield break;
                }
                state.ThrownWeaponEntity = thrown;

                // Stop navigator and face target
                StopNavigator(npc, brain, "ProcessThrowExplosive");
                brain.Navigator.SetFacingDirectionEntity(target);

                // waits 1.5 seconds
                yield return new WaitForSeconds(1.5f);

                // Check target still valid 
                if (target != null && !target.IsDestroyed && thrown != null && !thrown.IsDestroyed)
                {
                    // Just call ServerThrow on the weapon
                    thrown.ServerThrow(target.transform.position);
                    
                    // Track satchel throw for cycle management (if satchel)
                    if (isSatchel && state.SatchelPhase == WeaponState.SatchelCyclePhase.Throwing)
                    {
                        state.SatchelsThrownThisCycle++;
                        state.LastSatchelThrowTime = Time.time;
                    }
                    
                    // Set cooldown (15s for C4, our cooldown for satchels)
                    if (isSatchel)
                    {
                        state.NextSatchelThrowTime = Time.time + SatchelThrowCooldown;
                    }
                    else
                    {
                        // C4: 15s cooldown 
                        state.NextSatchelThrowTime = Time.time + 15f;
                    }
                }

                // Clear facing direction 
                brain.Navigator.ClearFacingDirectionOverride();
            }
            finally
            {
                // Always clear state
                state.IsThrowingSatchel = false;
                state.ThrownWeaponEntity = null;
                if (brain != null && brain.Navigator != null)
                {
                    brain.Navigator.ClearFacingDirectionOverride();
                }
            }
        }

        /// <summary>
        /// 🧨 SERVER-SIDE SATCHEL THROW: Proper throw implementation based on RaidingZombies.ServerThrow.
        /// Creates thrown entity server-side with proper velocity and angle calculation.
        /// </summary>
        private static void ServerThrowSatchel(ScientistNPC npc, ThrownWeapon wep, Vector3 targetPosition)
        {
            if (npc == null || wep == null) return;

            BasePlayer ownerPlayer = wep.GetOwnerPlayer();
            if (ownerPlayer == null) return;

            Vector3 position = ownerPlayer.eyes.position;
            Vector3 forward = ownerPlayer.eyes.BodyForward(); // NPC is already facing target (aim set in coroutine)
            float num1 = 1f;

            // Signal throw
            wep.SignalBroadcast(BaseEntity.Signal.Throw, string.Empty);

            // Create thrown entity (like RaidingZombies)
            BaseEntity entity = GameManager.server.CreateEntity(
                wep.prefabToThrow.resourcePath, 
                position, 
                Quaternion.LookRotation(wep.overrideAngle == Vector3.zero ? -forward : wep.overrideAngle)
            );
            if (entity == null) return;

            entity.creatorEntity = ownerPlayer;

            // Calculate aim direction with upward angle (like RaidingZombies)
            // Use BodyForward() since we already set aim direction in coroutine
            Vector3 aimDir = forward + Quaternion.AngleAxis(10f, Vector3.right) * Vector3.up;
            float throwSpeed = 5f;
            
            // Adjust throw speed based on distance for better accuracy
            float distance = Vector3.Distance(position, targetPosition);
            if (distance > 10f)
                throwSpeed = 6f; // Faster for longer throws
            else if (distance < 5f)
                throwSpeed = 4.5f; // Slightly slower for close throws
            
            // Validate throwSpeed (like RaidingZombies)
            if (float.IsNaN(throwSpeed))
            {
                aimDir = forward + Quaternion.AngleAxis(20f, Vector3.right) * Vector3.up;
                throwSpeed = 6f;
                if (float.IsNaN(throwSpeed))
                    throwSpeed = 5f;
            }

            // Set velocity
            entity.SetVelocity(aimDir * throwSpeed * num1);

            // Set angular velocity if needed
            if (wep.tumbleVelocity > 0f)
            {
                entity.SetAngularVelocity(new Vector3(
                    UnityEngine.Random.Range(-1f, 1f), 
                    UnityEngine.Random.Range(-1f, 1f), 
                    UnityEngine.Random.Range(-1f, 1f)
                ) * wep.tumbleVelocity);
            }

            // Disable dud chance (like RaidingZombies)
            DudTimedExplosive dud = entity.GetComponent<DudTimedExplosive>();
            if (dud != null)
                dud.dudChance = 0f;

            // Spawn the entity
            entity.Spawn();

            // Start attack cooldown
            wep.StartAttackCooldown(wep.repeatDelay);
        }


        private static void SetAim(ScientistNPC npc, Vector3 direction)
        {
            if (npc == null || direction == Vector3.zero) return;
            direction.Normalize();
            npc.viewAngles = Quaternion.LookRotation(direction).eulerAngles;
            npc.SetAimDirection(direction);
            npc.serverInput.current.aimAngles = npc.viewAngles;
        }

        internal static WeaponState GetState(ulong netId)
        {
            if (netId == 0) return null;

            if (!WeaponStates.TryGetValue(netId, out WeaponState state))
            {
                state = new WeaponState();
                WeaponStates[netId] = state;
            }

            return state;
        }

        private static void EquipItem(ScientistNPC npc, Item item)
        {
            if (npc == null || item == null) return;
            if (npc.GetActiveItem() != item)
                npc.UpdateActiveItem(item.uid);
        }

        private static void RestorePreviousItem(ScientistNPC npc, WeaponState state)
        {
            if (npc == null || state == null) return;
            if (state.PreviousActiveItemUid.IsValid && npc.GetActiveItem()?.uid != state.PreviousActiveItemUid)
                npc.UpdateActiveItem(state.PreviousActiveItemUid);
            state.PreviousActiveItemUid = default(ItemId);
        }

        private static void SyncGrenadeLauncherAmmo(Item grenadeLauncherItem, WeaponState state)
        {
            if (grenadeLauncherItem?.contents == null || state == null) return;

            Item ammoItem = grenadeLauncherItem.contents.itemList?.Find(item => item != null && item.info != null);
            if (ammoItem != null && !string.IsNullOrEmpty(ammoItem.info.shortname))
            {
                state.GrenadeLauncherAmmoType = ammoItem.info.shortname;
            }
        }

        private static bool HasStandardProjectile(ScientistNPC npc)
        {
            if (npc == null) return false;
            AttackEntity attackEntity = npc.GetAttackEntity();
            if (!(attackEntity is BaseProjectile)) return false;

            Item activeItem = npc.GetActiveItem();
            if (activeItem == null || activeItem.info == null) return true;

            string shortName = activeItem.info.shortname ?? string.Empty;
            return !IsSpecialRangedWeapon(shortName);
        }

        private static bool HasRocketLauncher(ScientistNPC npc) => FindRocketLauncher(npc) != null;

        private static bool HasGrenadeLauncher(ScientistNPC npc) => FindGrenadeLauncher(npc) != null;

        private static bool HasFlameThrower(ScientistNPC npc) => FindFlameThrower(npc) != null;

        private static bool HasThrownExplosive(ScientistNPC npc) => FindThrownExplosive(npc) != null;

        private static Item FindRocketLauncher(ScientistNPC npc)
        {
            return FindBeltItem(npc, item =>
            {
                string shortName = item.info?.shortname ?? string.Empty;
                return shortName == "rocket.launcher" || shortName.Contains("rocket.launcher");
            });
        }

        private static Item FindGrenadeLauncher(ScientistNPC npc)
        {
            return FindBeltItem(npc, item =>
            {
                string shortName = item.info?.shortname ?? string.Empty;
                return shortName == "multiplegrenadelauncher" || shortName == "grenade.launcher";
            });
        }

        private static Item FindFlameThrower(ScientistNPC npc)
        {
            return FindBeltItem(npc, item =>
            {
                string shortName = item.info?.shortname ?? string.Empty;
                return shortName == "flamethrower" || shortName == "military flamethrower";
            });
        }

        private static Item FindThrownExplosive(ScientistNPC npc)
        {
            return FindBeltItem(npc, item =>
            {
                string shortName = item.info?.shortname ?? string.Empty;
                return shortName == "explosive.timed" || shortName == "explosive.satchel";
            });
        }

        private static bool IsSpecialRangedWeapon(string shortName)
        {
            if (string.IsNullOrEmpty(shortName)) return false;

            return shortName == "rocket.launcher" ||
                   shortName.Contains("rocket.launcher") ||
                   shortName == "multiplegrenadelauncher" ||
                   shortName == "grenade.launcher" ||
                   shortName == "flamethrower" ||
                   shortName == "military flamethrower";
        }

        private static bool IsBowWeapon(string shortName)
        {
            if (string.IsNullOrEmpty(shortName)) return false;

            return shortName == "bow.compound" ||
                   shortName == "bow.hunting" ||
                   shortName == "legacy bow" ||
                   shortName == "crossbow" ||
                   shortName == "minicrossbow";
        }

        private static float GetBowDrawDelay(string shortName)
        {
            return shortName == "bow.compound" ? CompoundBowDrawDelay : BowDrawDelay;
        }

        private static Item FindBeltItem(ScientistNPC npc, Predicate<Item> predicate)
        {
            if (npc == null || npc.inventory == null || npc.inventory.containerBelt == null)
                return null;

            var items = npc.inventory.containerBelt.itemList;
            if (items == null) return null;

            for (int i = 0; i < items.Count; i++)
            {
                Item item = items[i];
                if (item != null && predicate(item))
                    return item;
            }

            return null;
        }

        private static float GetFlameThrowerRange(ScientistNPC npc)
        {
            // Get effectiveRange from equipped flamethrower, or default to 5f
            FlameThrower flameThrower = npc.GetHeldEntity() as FlameThrower;
            if (flameThrower != null && flameThrower.effectiveRange > 0f)
                return flameThrower.effectiveRange;
            
            // Try to get from belt item
            Item flameThrowerItem = FindFlameThrower(npc);
            if (flameThrowerItem != null)
            {
                AttackEntity attackEntity = flameThrowerItem.GetHeldEntity() as AttackEntity;
                if (attackEntity != null && attackEntity.effectiveRange > 0f)
                    return attackEntity.effectiveRange;
            }
            
            // Default range (2-5f is ideal, but we use 5f for selection threshold)
            return 5f;
        }

        /// <summary>
        /// 🎯 GetTypeWeaponItem: Gets weapon type based on shortname.
        /// Returns: 0=melee, 1=close range, 2=medium range, 3=long range, 4=sniper, -1=unknown
        /// Used for distance-based weapon selection.
        /// </summary>
        private static int GetTypeWeaponItem(string shortName)
        {
            if (string.IsNullOrEmpty(shortName)) return -1;

            // Type 0: Melee weapons
            if (IsMeleeWeapon(shortName)) return 0;

            // Type 1: Close range weapons (bows, shotguns, pistols, flamethrower)
            if (IsCloseRangeWeapon(shortName)) return 1;

            // Type 2: Medium range weapons (SMGs, semi-auto rifles)
            if (IsMediumRangeWeapon(shortName)) return 2;

            // Type 3: Long range weapons (AK, LR300, M249, etc.)
            if (IsLongRangeWeapon(shortName)) return 3;

            // Type 4: Sniper weapons (bolt, L96)
            if (IsSniperWeapon(shortName)) return 4;

            return -1;
        }

        private static bool IsMeleeWeapon(string shortName)
        {
            return shortName == "bone.club" || shortName == "knife.bone" || shortName == "knife.butcher" ||
                   shortName == "candycaneclub" || shortName == "knife.combat" || shortName == "longsword" ||
                   shortName == "mace" || shortName == "machete" || shortName == "paddle" ||
                   shortName == "pitchfork" || shortName == "salvaged.cleaver" || shortName == "salvaged.sword" ||
                   shortName == "spear.stone" || shortName == "spear.wooden" || shortName == "chainsaw" ||
                   shortName == "hatchet" || shortName == "jackhammer" || shortName == "pickaxe" ||
                   shortName == "axe.salvaged" || shortName == "hammer.salvaged" || shortName == "icepick.salvaged" ||
                   shortName == "stonehatchet" || shortName == "stone.pickaxe" || shortName == "torch" ||
                   shortName == "sickle" || shortName == "rock" || shortName == "snowball" ||
                   shortName == "mace.baseballbat" || shortName == "concretepickaxe" || shortName == "concretehatchet" ||
                   shortName == "lumberjack.hatchet" || shortName == "lumberjack.pickaxe" ||
                   shortName == "diverhatchet" || shortName == "diverpickaxe" || shortName == "divertorch" ||
                   shortName == "knife.skinning" || shortName == "vampire.stake" || shortName == "shovel" ||
                   shortName == "spear.cny" || shortName == "frontier_hatchet" || shortName == "boomerang";
        }

        private static bool IsCloseRangeWeapon(string shortName)
        {
            return shortName == "speargun" || shortName == "bow.compound" || shortName == "crossbow" ||
                   shortName == "bow.hunting" || shortName == "shotgun.double" || shortName == "pistol.eoka" ||
                   shortName == "flamethrower" || shortName == "pistol.m92" || shortName == "pistol.nailgun" ||
                   shortName == "multiplegrenadelauncher" || shortName == "shotgun.pump" ||
                   shortName == "pistol.python" || shortName == "pistol.revolver" || shortName == "pistol.semiauto" ||
                   shortName == "pistol.prototype17" || shortName == "snowballgun" || shortName == "shotgun.spas12" ||
                   shortName == "shotgun.waterpipe" || shortName == "shotgun.m4" || shortName == "legacy bow" ||
                   shortName == "military flamethrower" || shortName == "blunderbuss" ||
                   shortName == "minicrossbow" || shortName == "blowpipe";
        }

        private static bool IsMediumRangeWeapon(string shortName)
        {
            return shortName == "smg.2" || shortName == "smg.mp5" || shortName == "rifle.semiauto" ||
                   shortName == "smg.thompson" || shortName == "rifle.sks" || shortName == "revolver.hc" ||
                   shortName == "t1_smg";
        }

        private static bool IsLongRangeWeapon(string shortName)
        {
            return shortName == "rifle.ak" || shortName == "rifle.lr300" || shortName == "lmg.m249" ||
                   shortName == "rifle.m39" || shortName == "hmlmg" || shortName == "rifle.ak.ice" ||
                   shortName == "rifle.ak.diver" || shortName == "minigun" || shortName == "rifle.ak.med";
        }

        private static bool IsSniperWeapon(string shortName)
        {
            return shortName == "rifle.bolt" || shortName == "rifle.l96";
        }

        /// <summary>
        /// 🎯 weapon selection: Selects best weapon based on distance to target.
        /// Uses weapon type system (0=melee, 1-4=ranged tiers) to choose optimal weapon.
        /// </summary>
        public static Item SelectWeaponByDistance(ScientistNPC npc, float distanceToTarget, float attackRangeMultiplier = 1f)
        {
            if (npc == null || npc.inventory == null || npc.inventory.containerBelt == null)
                return null;

            var beltItems = npc.inventory.containerBelt.itemList;
            if (beltItems == null || beltItems.Count == 0) return null;

            Item bestWeapon = null;
            int bestType = -1;

            foreach (Item item in beltItems)
            {
                if (item == null || item.info == null) continue;

                int weaponType = GetTypeWeaponItem(item.info.shortname);
                if (weaponType == -1) continue;

                // Calculate effective range for this weapon type
                float weaponRange = weaponType > 0 ? attackRangeMultiplier * weaponType * 10f : 2f;

                if (bestType == -1)
                {
                    // First valid weapon
                    bestWeapon = item;
                    bestType = weaponType;
                }
                else
                {
                    // logic: Choose weapon that best matches distance
                    float oldRange = bestType > 0 ? attackRangeMultiplier * bestType * 10f : 2f;
                    float newRange = weaponRange;

                    // Prefer weapon that:
                    // 1. Is closer to target distance than current best
                    // 2. Or is within range when current is not
                    if ((oldRange > distanceToTarget && newRange > distanceToTarget && newRange < oldRange) ||
                        (oldRange < distanceToTarget && newRange > distanceToTarget) ||
                        (oldRange < distanceToTarget && newRange < distanceToTarget && newRange > oldRange))
                    {
                        bestWeapon = item;
                        bestType = weaponType;
                    }
                }
            }

            return bestWeapon;
        }

        private static bool HasLineOfSight(ScientistNPC npc, BaseEntity target)
        {
            if (npc == null || target == null) return false;

            try
            {
                return npc.CanSeeTarget(target);
            }
            catch
            {
                return true;
            }
        }
    }
}
