using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Network;
using Rust;
using Rust.Ai;
using UnityEngine;

namespace HarmonyMods.RustGame.Nivex.SafeZonePVE
{
	public class Manager : IHarmonyModHooks
	{
		public class PatchDefinition
		{
			public readonly HarmonyPatchType HarmonyPatchType;
			public readonly Type TargetClassType;
			public readonly string TargetMethodName;
			public readonly Type[] TargetMethodParameterTypes;
			public readonly Type PatchClassType;
			public readonly string PatchMethodName;

			public MethodInfo TargetMethod { get; set; }
			public MethodInfo PatchMethod { get; set; }
			public bool Applied { get; set; }

			public PatchDefinition(HarmonyPatchType harmonyPatchType, Type targetClassType, string targetMethodName, Type[] targetMethodParameterTypes, Type patchClassType, string patchMethodName)
			{
				HarmonyPatchType = harmonyPatchType;
				TargetClassType = targetClassType;
				TargetMethodName = targetMethodName;
				TargetMethodParameterTypes = targetMethodParameterTypes;
				PatchClassType = patchClassType;
				PatchMethodName = patchMethodName;
			}
		}

		private class TriggerBase_OnEntityEnter
		{
			internal static List<NetworkableId> CanVehicleTakeDamage;

			internal static void Prefix(BaseEntity ent, TriggerBase __instance)
			{
				OnEntityEnter(ent, __instance);
			}

			internal static void OnEntityEnter(BaseEntity ent, TriggerBase __instance)
			{
				try
				{
					if (ent.Cast<BaseHelicopter>(out var component) && component.net != null && !CanVehicleTakeDamage.Contains(component.net.ID) && component.InSafeZone())
					{
						NetworkableId uid = component.net.ID;
						CanVehicleTakeDamage.Add(uid);
						((FacepunchBehaviour)component).Invoke((Action)delegate
						{
							CanVehicleTakeDamage.Remove(uid);
						}, 15f);
					}
				}
				catch
				{
				}
			}
		}

		private class BradleyAPC_VisibilityTest
		{
			internal static bool Prefix(BaseEntity ent)
			{
				return OnVisibilityTest(ent);
			}

			internal static bool OnVisibilityTest(BaseEntity ent)
			{
				if (ent.Cast<BasePlayer>(out var component) && !component.UserIDString.IsSteamId())
				{
					return false;
				}
				return true;
			}
		}

		private class SimpleAIMemory_SetKnown
		{
			internal static bool Prefix(BaseEntity ent, BaseEntity owner, AIBrainSenses brainSenses)
			{
				return CanBeTargeted(ent, owner);
			}
		}

		private class HumanNPC_GetBestTarget
		{
			internal static void Postfix(ref BaseEntity __result, HumanNPC __instance)
			{
				OnGetBestTarget(ref __result, __instance);
			}

			internal static void OnGetBestTarget(ref BaseEntity __result, HumanNPC __instance)
			{
				if (!CanBeTargeted(__result, __instance))
				{
					__result = null;
				}
			}
		}

		private class AIBrainSenses_GetNearest
		{
			internal static void Postfix(List<BaseEntity> entities, float rangeFraction, ref BaseEntity __result, BaseEntity ___owner, AIBrainSenses __instance)
			{
				OnGetNearest(ref __result, ___owner, __instance);
			}

			internal static void OnGetNearest(ref BaseEntity __result, BaseEntity ___owner, AIBrainSenses __instance)
			{
				if (!CanBeTargeted(__result, ___owner))
				{
					__result = null;
				}
			}
		}

		private class BaseNpc_WantsToAttack
		{
			internal static bool Prefix(BaseEntity target, ref float __result, BaseNpc __instance)
			{
				return OnWantsToAttack(target, ref __result, __instance);
			}

			internal static bool OnWantsToAttack(BaseEntity target, ref float __result, BaseNpc __instance)
			{
				if (!CanBeTargeted(target, __instance))
				{
					__result = 0f;
					return false;
				}
				return true;
			}
		}

		private class BaseHelicopter_CollisionDamageEnabled
		{
			internal static bool Prefix(BaseHelicopter __instance, ref bool __result)
			{
				return OnProcessCollision(__instance, ref __result);
			}

			internal static bool OnProcessCollision(BaseHelicopter __instance, ref bool __result)
			{
				try
				{
					if (__instance.IsValid() && __instance.InSafeZone() && !TriggerBase_OnEntityEnter.CanVehicleTakeDamage.Contains(__instance.net.ID))
					{
						__result = false;
						return false;
					}
				}
				catch
				{
				}
				return true;
			}
		}

		private class BasePlayer_Hurt
		{
			internal static bool Prefix(HitInfo info, BasePlayer __instance)
			{
				return OnEntityTakeDamage(__instance, info);
			}

			internal static bool OnEntityTakeDamage(BasePlayer __instance, HitInfo info)
			{
				try
				{
					if ((UnityEngine.Object)(object)__instance != (UnityEngine.Object)null && __instance.UserIDString.IsSteamId() && !__instance.IsDead() && __instance.InSafeZone() && !__instance.IsHostile() && (!__instance.IsImmortalTo(info) || !(info.damageTypes.Total() >= 0f)))
					{
						if (info.Initiator is BaseMountable || info.Initiator is FireBall)
						{
							info.damageTypes = new DamageTypeList();
							return false;
						}
						if ((UnityEngine.Object)(object)info.Initiator != (UnityEngine.Object)(object)__instance && info.Initiator.Cast<BasePlayer>(out var component) && !component.IsAdmin && component.InSafeZone())
						{
							component.health -= info.damageTypes.Total();
							component.SendNetworkUpdate();
							if (component.Health() <= 0f)
							{
								component.Die(new HitInfo(component, component, info.damageTypes.GetMajorityDamageType(), 9999f));
							}
							info.damageTypes = new DamageTypeList();
							return false;
						}
					}
				}
				catch
				{
				}
				return true;
			}
		}

		private class BasePlayer_OnAttacked
		{
			internal static bool Prefix(HitInfo info, BasePlayer __instance)
			{
				return BasePlayer_Hurt.OnEntityTakeDamage(__instance, info);
			}
		}

		private class BaseCombatEntity_Hurt
		{
			internal static bool Prefix(HitInfo info, BaseCombatEntity __instance, List<TriggerBase> ___triggers)
			{
				return OnEntityTakeDamage(info, __instance, ___triggers);
			}

			internal static bool OnEntityTakeDamage(HitInfo info, BaseCombatEntity __instance, List<TriggerBase> ___triggers)
			{
				try
				{
					if ((UnityEngine.Object)(object)__instance != (UnityEngine.Object)null && __instance is BasePlayer basePlayer && basePlayer.InSafeZone())
					{
						if (!basePlayer.UserIDString.IsSteamId())
						{
							return true;
						}
						info.damageTypes = new DamageTypeList();
						return false;
					}
				}
				catch
				{
				}
				return true;
			}
		}

		private class NPCAutoTurret_IsEntityHostile
		{
			internal static bool Prefix(BaseCombatEntity ent, NPCAutoTurret __instance)
			{
				return CanEntityBeHostile(ent, __instance);
			}

			internal static bool CanEntityBeHostile(BaseEntity __result, BaseEntity __instance)
			{
				return CanBeTargeted(__result, __instance);
			}
		}

		private readonly List<PatchDefinition> _permanent = new List<PatchDefinition>();

		private const string Name = "SafeZonePVE";

		private Harmony _harmony;

		private static MethodInfo _clientRpcFloat;

		public void OnLoaded(OnHarmonyModLoadedArgs args)
		{
			TriggerBase_OnEntityEnter.CanVehicleTakeDamage = new List<NetworkableId>();
			_clientRpcFloat = typeof(BaseEntity).GetMethod("ClientRPC", new Type[] { typeof(RpcTarget), typeof(float) });
			PatchAll();
			Debug.LogWarning((object)"[Harmony] Loaded: SafeZonePVE 1.0.0.2 by nivex");
		}

		public void OnUnloaded(OnHarmonyModUnloadedArgs args)
		{
			UnpatchAll();
			TriggerBase_OnEntityEnter.CanVehicleTakeDamage = null;
			_clientRpcFloat = null;
			Debug.LogWarning((object)"[Harmony] Unloaded: SafeZonePVE 1.0.0.2 by nivex");
		}

		private static void Puts(string message)
		{
			Debug.LogWarning((object)("[Harmony:: SafeZonePVE] " + message));
		}

		private static void SendSetHostileLength(BasePlayer player, float duration)
		{
			try
			{
				if (_clientRpcFloat != null)
				{
					_clientRpcFloat.Invoke(player, new object[] { RpcTarget.Player("SetHostileLength", player), duration });
				}
			}
			catch
			{
			}
		}

		private void PatchAll()
		{
			if (ValidatePatchDefinitions())
			{
				PatchAll(_permanent);
			}
		}

		private bool ValidatePatchDefinitions()
		{
			_permanent.Add(new PatchDefinition((HarmonyPatchType)1, typeof(TriggerBase), "OnEntityEnter", new Type[1] { typeof(BaseEntity) }, typeof(TriggerBase_OnEntityEnter), "Prefix"));
			_permanent.Add(new PatchDefinition((HarmonyPatchType)1, typeof(BradleyAPC), "VisibilityTest", new Type[1] { typeof(BaseEntity) }, typeof(BradleyAPC_VisibilityTest), "Prefix"));
			_permanent.Add(new PatchDefinition((HarmonyPatchType)1, typeof(SimpleAIMemory), "SetKnown", new Type[3]
			{
				typeof(BaseEntity),
				typeof(BaseEntity),
				typeof(AIBrainSenses)
			}, typeof(SimpleAIMemory_SetKnown), "Prefix"));
			_permanent.Add(new PatchDefinition((HarmonyPatchType)2, typeof(HumanNPC), "GetBestTarget", Type.EmptyTypes, typeof(HumanNPC_GetBestTarget), "Postfix"));
			_permanent.Add(new PatchDefinition((HarmonyPatchType)2, typeof(AIBrainSenses), "GetNearest", new Type[2]
			{
				typeof(List<BaseEntity>),
				typeof(float)
			}, typeof(AIBrainSenses_GetNearest), "Postfix"));
			_permanent.Add(new PatchDefinition((HarmonyPatchType)1, typeof(BaseNpc), "WantsToAttack", new Type[1] { typeof(BaseEntity) }, typeof(BaseNpc_WantsToAttack), "Prefix"));
			_permanent.Add(new PatchDefinition((HarmonyPatchType)1, typeof(BaseHelicopter), "CollisionDamageEnabled", Type.EmptyTypes, typeof(BaseHelicopter_CollisionDamageEnabled), "Prefix"));
			_permanent.Add(new PatchDefinition((HarmonyPatchType)1, typeof(BasePlayer), "OnAttacked", new Type[1] { typeof(HitInfo) }, typeof(BasePlayer_OnAttacked), "Prefix"));
			_permanent.Add(new PatchDefinition((HarmonyPatchType)1, typeof(NPCAutoTurret), "IsEntityHostile", new Type[1] { typeof(BaseCombatEntity) }, typeof(NPCAutoTurret_IsEntityHostile), "Prefix"));
			if (!ValidatePatchDefinitions(_permanent))
			{
				_permanent.Clear();
				return false;
			}
			return true;
		}

		private bool ValidatePatchDefinitions(List<PatchDefinition> definitions)
		{
			for (int i = 0; i < definitions.Count; i++)
			{
				PatchDefinition patchDefinition = definitions[i];
				MethodInfo methodInfo = AccessTools.Method(patchDefinition.TargetClassType, patchDefinition.TargetMethodName, patchDefinition.TargetMethodParameterTypes, (Type[])null);
				if (methodInfo == null)
				{
					Puts(string.Format("Failed to find target method '{0}' with specified parameters.", patchDefinition.TargetClassType));
					return false;
				}
				MethodInfo methodInfo2 = AccessTools.Method(patchDefinition.PatchClassType, patchDefinition.PatchMethodName, (Type[])null, (Type[])null);
				if (methodInfo2 == null)
				{
					Puts(string.Format("Failed to find patch '{0} ({1})' method.", patchDefinition.PatchClassType, patchDefinition.PatchMethodName));
					return false;
				}
				patchDefinition.PatchMethod = methodInfo2;
				patchDefinition.TargetMethod = methodInfo;
			}
			return true;
		}

		private void PatchAll(List<PatchDefinition> definitions)
		{
			if (_harmony == null)
			{
				_harmony = new Harmony("SafeZonePVEPatch");
			}
			foreach (PatchDefinition definition in definitions)
			{
				if (!definition.Applied && !(definition.PatchMethod == null) && !(definition.TargetMethod == null))
				{
					switch (definition.HarmonyPatchType)
					{
					case (HarmonyPatchType)1:
						_harmony.Patch((MethodBase)definition.TargetMethod, new HarmonyMethod(definition.PatchMethod), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
						definition.Applied = true;
						break;
					case (HarmonyPatchType)2:
						_harmony.Patch((MethodBase)definition.TargetMethod, (HarmonyMethod)null, new HarmonyMethod(definition.PatchMethod), (HarmonyMethod)null, (HarmonyMethod)null);
						definition.Applied = true;
						break;
					case (HarmonyPatchType)3:
						_harmony.Patch((MethodBase)definition.TargetMethod, (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(definition.PatchMethod), (HarmonyMethod)null);
						definition.Applied = true;
						break;
					case (HarmonyPatchType)4:
						_harmony.Patch((MethodBase)definition.TargetMethod, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(definition.PatchMethod));
						definition.Applied = true;
						break;
					default:
						continue;
					}
					Puts(string.Format("Patched {0} {1} {2}", definition.TargetMethod.DeclaringType, definition.TargetMethod, definition.PatchMethod));
				}
			}
		}

		private void UnpatchAll()
		{
			UnpatchAll(_permanent);
		}

		private void UnpatchAll(List<PatchDefinition> definitions)
		{
			if (_harmony == null)
			{
				return;
			}
			foreach (PatchDefinition definition in definitions)
			{
				if (definition.Applied && !(definition.TargetMethod == null))
				{
					_harmony.Unpatch((MethodBase)definition.TargetMethod, definition.HarmonyPatchType, _harmony.Id);
					definition.Applied = false;
				}
			}
		}

		public static bool CanBeTargeted(BaseEntity __result, BaseEntity __instance)
		{
			try
			{
				BasePlayer basePlayer = __result as BasePlayer;
				if ((UnityEngine.Object)(object)basePlayer != (UnityEngine.Object)null && basePlayer.UserIDString.IsSteamId() && (basePlayer.limitNetworking || (__instance.ShortPrefabName != "scientistnpc_peacekeeper" && basePlayer.InSafeZone())))
				{
					if (basePlayer.State.unHostileTimestamp > TimeEx.currentTimestamp)
					{
						basePlayer.State.unHostileTimestamp = TimeEx.currentTimestamp;
						basePlayer.DirtyPlayerState();
						SendSetHostileLength(basePlayer, 0f);
					}
					return false;
				}
			}
			catch
			{
			}
			return true;
		}
	}
}
