using System.Collections.Generic;
using UnityEngine;

namespace Convoy
{
    /// <summary>
    /// Oxide Convoy TurretOptimizer parity: disable vanilla AutoTurret trigger scanning and
    /// only feed real players into entityContents. Prevents convoy turrets from targeting
    /// BradleyAPC / other convoy vehicles.
    /// </summary>
    public sealed class ConvoyTurretOptimizer : FacepunchBehaviour
    {
        private AutoTurret _autoTurret;
        private float _targetRadius;

        public static void Attach(AutoTurret autoTurret, float targetRadius)
        {
            if (autoTurret == null) return;
            var existing = autoTurret.GetComponent<ConvoyTurretOptimizer>();
            if (existing != null) return;

            var opt = autoTurret.gameObject.AddComponent<ConvoyTurretOptimizer>();
            opt.Init(autoTurret, targetRadius);
        }

        private void Init(AutoTurret autoTurret, float targetRadius)
        {
            _autoTurret = autoTurret;
            _targetRadius = targetRadius > 0f ? targetRadius : 30f;
            AutoTurret.interferenceUpdateList.Remove(autoTurret);

            if (autoTurret.targetTrigger != null)
            {
                var sphereCollider = autoTurret.targetTrigger.GetComponent<SphereCollider>();
                if (sphereCollider != null)
                    sphereCollider.enabled = false;
            }

            autoTurret.Invoke(() =>
            {
                if (autoTurret == null || autoTurret.IsDestroyed) return;
                autoTurret.CancelInvoke(autoTurret.ServerDo);
                autoTurret.CancelInvoke(autoTurret.ServerThink);
                autoTurret.SetTarget(null);
            }, 1.1f);

            autoTurret.InvokeRepeating(OptimizedServerTick, UnityEngine.Random.Range(1.2f, 2.2f), 0.015f);
            autoTurret.InvokeRepeating(ScanTargets, 3f, 1f);
        }

        private void ScanTargets()
        {
            if (_autoTurret == null || _autoTurret.IsDestroyed || _autoTurret.targetTrigger == null)
                return;

            if (_autoTurret.targetTrigger.entityContents == null)
                _autoTurret.targetTrigger.entityContents = new HashSet<BaseEntity>();
            else
                _autoTurret.targetTrigger.entityContents.Clear();

            var ec = EventController.Instance;
            if (ec == null || !ec.IsAggressive())
                return;

            int count = BaseEntity.Query.Server.GetPlayersInSphereFast(
                transform.position,
                _targetRadius,
                AIBrainSenses.playerQueryResults,
                IsPlayerCanBeTargeted);

            if (count == 0)
                return;

            _autoTurret.authDirty = true;

            for (int i = 0; i < count; i++)
            {
                BasePlayer player = AIBrainSenses.playerQueryResults[i];
                if (player == null) continue;
                if (player.IsSleeping() || (player.InSafeZone() && !player.IsHostile()))
                    continue;

                _autoTurret.targetTrigger.entityContents.Add(player);
            }
        }

        public void OptimizedServerTick()
        {
            if (_autoTurret == null || _autoTurret.isClient || _autoTurret.IsDestroyed)
                return;

            float timeSinceLastServerTick = (float)_autoTurret.timeSinceLastServerTick;
            _autoTurret.timeSinceLastServerTick = 0;

            if (!_autoTurret.IsOnline())
            {
                _autoTurret.OfflineTick();
            }
            else if (!_autoTurret.IsBeingControlled)
            {
                if (!_autoTurret.HasTarget())
                    _autoTurret.IdleTick(timeSinceLastServerTick);
                else
                    OptimizedTargetTick();
            }

            _autoTurret.UpdateFacingToTarget(timeSinceLastServerTick);

            if (_autoTurret.totalAmmoDirty && Time.time > _autoTurret.nextAmmoCheckTime)
            {
                _autoTurret.UpdateTotalAmmo();
                _autoTurret.totalAmmoDirty = false;
                _autoTurret.nextAmmoCheckTime = Time.time + 0.5f;
            }
        }

        public void OptimizedTargetTick()
        {
            if (Time.realtimeSinceStartup >= _autoTurret.nextVisCheck)
            {
                _autoTurret.nextVisCheck = Time.realtimeSinceStartup + UnityEngine.Random.Range(0.2f, 0.3f);
                _autoTurret.targetVisible = _autoTurret.ObjectVisible(_autoTurret.target);
                if (_autoTurret.targetVisible)
                    _autoTurret.lastTargetSeenTime = Time.realtimeSinceStartup;
            }

            _autoTurret.EnsureReloaded();
            BaseProjectile attachedWeapon = _autoTurret.GetAttachedWeapon();

            if (Time.time >= _autoTurret.nextShotTime
                && _autoTurret.targetVisible
                && Mathf.Abs(_autoTurret.AngleToTarget(_autoTurret.target, _autoTurret.currentAmmoGravity != 0f)) < _autoTurret.GetMaxAngleForEngagement())
            {
                if (attachedWeapon != null)
                {
                    if (attachedWeapon.primaryMagazine.contents > 0)
                    {
                        _autoTurret.FireAttachedGun(
                            _autoTurret.AimOffset(_autoTurret.target),
                            _autoTurret.aimCone,
                            _autoTurret.PeacekeeperMode() ? _autoTurret.target : null,
                            float.PositiveInfinity,
                            float.NegativeInfinity);
                        float delay = attachedWeapon.isSemiAuto
                            ? attachedWeapon.repeatDelay * 1.5f
                            : attachedWeapon.repeatDelay;
                        delay = attachedWeapon.ScaleRepeatDelay(delay);
                        _autoTurret.nextShotTime = Time.time + delay;
                    }
                    else
                    {
                        _autoTurret.nextShotTime = Time.time + 5f;
                    }
                }
                else if (_autoTurret.HasFallbackWeapon())
                {
                    _autoTurret.FireGun(_autoTurret.AimOffset(_autoTurret.target), _autoTurret.aimCone, null, _autoTurret.target);
                    _autoTurret.nextShotTime = Time.time + 0.115f;
                }
                else if (_autoTurret.HasGenericFireable())
                {
                    _autoTurret.AttachedWeapon.ServerUse();
                    _autoTurret.nextShotTime = Time.time + 0.115f;
                }
                else
                {
                    _autoTurret.nextShotTime = Time.time + 1f;
                }
            }

            BasePlayer targetPlayer = _autoTurret.target as BasePlayer;
            if (_autoTurret.target != null
                && (!targetPlayer.IsRealPlayer()
                    || _autoTurret.target.IsDead()
                    || Time.realtimeSinceStartup - _autoTurret.lastTargetSeenTime > 3f
                    || Vector3.Distance(_autoTurret.transform.position, _autoTurret.target.transform.position) > _autoTurret.sightRange
                    || (_autoTurret.PeacekeeperMode() && !_autoTurret.IsEntityHostile(_autoTurret.target))))
            {
                _autoTurret.SetTarget(null);
            }
        }

        private static bool IsPlayerCanBeTargeted(BasePlayer player)
        {
            if (!player.IsRealPlayer())
                return false;
            if (player.IsDead() || player.IsSleeping() || player.IsWounded())
                return false;
            if (player.InSafeZone() || player.limitNetworking)
                return false;
            return true;
        }

    }
}
