using System;
using System.Collections.Generic;
using HarmonyTests.Lib;
using UnityEngine;

namespace FullRangeAutoturrets.Lib;

public class FlameTurretAIBrain : SingletonComponent<FlameTurretAIBrain>
{
	internal static void Initialize()
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		new GameObject().AddComponent<FlameTurretAIBrain>();
	}

	internal bool PlayerIsVanished(BasePlayer player)
	{
		return Helpers.GetFieldValue<bool>(player, "_limitedNetworking");
	}

	internal float AngleToTarget(FlameTurret instance, BaseCombatEntity potentialtarget)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0020: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0031: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		BasePlayer basePlayer = potentialtarget as BasePlayer;
		Transform eyeTransform = instance.eyeTransform;
		Vector3 val = Vector3Ex.Direction2D(basePlayer.eyes.position, eyeTransform.position);
		Vector3 val2 = Vector3Ex.XZ3D(eyeTransform.forward);
		return Vector3.Angle(val2.normalized, val);
	}

	internal List<BaseEntity> DetectPlayersInRange(FlameTurret turret, float maxRangeFromOrigin = 90f)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		List<BasePlayer> list = new List<BasePlayer>();
		Vis.Entities(((Component)turret).transform.position, 9f, list, -1, (QueryTriggerInteraction)2);
		float num = maxRangeFromOrigin / 2f;
		List<BaseEntity> list2 = new List<BaseEntity>();
		foreach (BasePlayer item in list)
		{
			if (!item.IsNpc && !item.IsSleeping() && !item.IsDead() && !item.IsBuildingAuthed() && !PlayerIsVanished(item) && (turret.IsVisible(((Component)item).transform.position, 9f) || turret.IsVisible(item.eyes.position, 9f)))
			{
				float num2 = AngleToTarget(turret, item);
				if ((float)Math.Floor(num2) <= (float)Math.Ceiling(num))
				{
					list2.Add(item);
				}
			}
		}
		return list2;
	}

	internal bool EvalTargetsInRange(FlameTurret instance)
	{
		float maxRangeFromOrigin = (float)Main.instance.Config.Get("FlameTurrets.DetectRange");
		List<BaseEntity> list = DetectPlayersInRange(instance, maxRangeFromOrigin);
		if (list.Count == 0)
		{
			instance.SetTriggered(triggered: false);
			((BaseNetworkable)instance).SendNetworkUpdateImmediate();
			return false;
		}
		instance.trigger.entityContents = new HashSet<BaseEntity>(list);
		instance.SetTriggered(triggered: true);
		((BaseNetworkable)instance).SendNetworkUpdateImmediate();
		return true;
	}
}
