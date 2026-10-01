using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CustomGenerator.Shared;
using HarmonyLib;
using UnityEngine;

namespace CustomGenerator.Server;

internal static class IOModule
{
	private const float MatchDistance = 1f;

	private static readonly FieldInfo CounterTarget = AccessTools.Field(typeof(PowerCounter), "targetCounterNumber");

	public static void Apply(List<IOEntityInfo> io)
	{
		if (io == null || io.Count == 0)
		{
			return;
		}
		Dictionary<string, List<BaseEntity>> index = BuildIndex(io);
		List<(IOEntityInfo, BaseEntity)> list = new List<(IOEntityInfo, BaseEntity)>();
		int num = 0;
		foreach (IOEntityInfo item in io)
		{
			BaseEntity baseEntity = Find(index, item.Prefab, item.Position);
			if ((Object)(object)baseEntity == (Object)null)
			{
				if (num++ < 5)
				{
					Log.Warning("IO: no " + item.Prefab + " at " + Format(item.Position));
				}
			}
			else
			{
				list.Add((item, baseEntity));
				ApplySettings(baseEntity, item);
			}
		}
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		HashSet<IOEntity> hashSet = new HashSet<IOEntity>();
		foreach (var item2 in list)
		{
			var (iOEntityInfo, _) = item2;
			if (!(item2.Item2 is IOEntity iOEntity))
			{
				continue;
			}
			for (int i = 0; i < iOEntityInfo.Outputs.Count; i++)
			{
				IOConnectionInfo iOConnectionInfo = iOEntityInfo.Outputs[i];
				if (iOConnectionInfo == null)
				{
					continue;
				}
				IOEntity iOEntity2 = Find(index, iOConnectionInfo.Prefab, iOConnectionInfo.Position) as IOEntity;
				string text = (((Object)(object)iOEntity2 == (Object)null) ? (Short(iOConnectionInfo.Prefab) + " not found at " + Format(iOConnectionInfo.Position)) : ((i >= iOEntity.outputs.Length) ? $"{Short(iOEntityInfo.Prefab)} has {iOEntity.outputs.Length} outputs now, the monument uses output {i}" : ((iOConnectionInfo.Slot < 0 || iOConnectionInfo.Slot >= iOEntity2.inputs.Length) ? $"{Short(iOConnectionInfo.Prefab)} has {iOEntity2.inputs.Length} inputs now, the monument uses input {iOConnectionInfo.Slot}" : null)));
				if (text != null)
				{
					if (num4++ < 10)
					{
						Log.Warning($"IO: can't connect {Short(iOEntityInfo.Prefab)} output {i} to {Short(iOConnectionInfo.Prefab)} input {iOConnectionInfo.Slot}: {text}");
					}
					continue;
				}
				IOEntity.IOSlot iOSlot = iOEntity.outputs[i];
				IOEntity.IOSlot iOSlot2 = iOEntity2.inputs[iOConnectionInfo.Slot];
				if ((Object)(object)iOSlot.connectedTo.Get() == (Object)(object)iOEntity2 && iOSlot.connectedToSlot == iOConnectionInfo.Slot)
				{
					num3++;
					continue;
				}
				iOSlot.connectedTo.Set(iOEntity2);
				iOSlot.connectedToSlot = iOConnectionInfo.Slot;
				iOSlot.connectedTo.Init();
				iOSlot2.connectedTo.Set(iOEntity);
				iOSlot2.connectedToSlot = i;
				iOSlot2.connectedTo.Init();
				hashSet.Add(iOEntity);
				hashSet.Add(iOEntity2);
				num2++;
			}
		}
		foreach (IOEntity item3 in hashSet)
		{
			item3.MarkDirtyForceUpdateOutputs();
			item3.SendNetworkUpdate();
		}
		Log.Info($"IO: {list.Count}/{io.Count} entities found, {num2} connections made" + ((num3 > 0) ? $", {num3} already connected" : "") + ((num4 > 0) ? $", {num4} failed" : "") + ((num > 0) ? $", {num} not found" : ""));
		List<IOEntity> entities = list.Select<(IOEntityInfo, BaseEntity), BaseEntity>(((IOEntityInfo Info, BaseEntity Entity) x) => x.Entity).OfType<IOEntity>().ToList();
		((FacepunchBehaviour)SingletonComponent<ServerMgr>.Instance).Invoke((Action)delegate
		{
			int num5 = entities.Count((IOEntity x) => (Object)(object)x != (Object)null && !x.IsDestroyed && x.IsPowered());
			Log.Info($"IO: {num5}/{entities.Count} entities have power");
		}, 5f);
	}

	private static Dictionary<string, List<BaseEntity>> BuildIndex(List<IOEntityInfo> io)
	{
		//IL_00e5: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ea: Unknown result type (might be due to invalid IL or missing references)
		Dictionary<string, List<BaseEntity>> dictionary = new HashSet<string>(io.Select((IOEntityInfo x) => x.Prefab).Concat(from x in io.SelectMany((IOEntityInfo x) => x.Inputs.Concat(x.Outputs))
			where x != null
			select x.Prefab)).ToDictionary((string x) => x, (string x) => new List<BaseEntity>());
		Enumerator<BaseNetworkable> enumerator = BaseNetworkable.serverEntities.GetEnumerator();
		try
		{
			while (enumerator.MoveNext())
			{
				if (enumerator.Current is BaseEntity baseEntity && dictionary.TryGetValue(baseEntity.PrefabName, out var value))
				{
					value.Add(baseEntity);
				}
			}
			return dictionary;
		}
		finally
		{
			((IDisposable)enumerator/*cast due to .constrained prefix*/).Dispose();
		}
	}

	private static BaseEntity Find(Dictionary<string, List<BaseEntity>> index, string prefab, float[] position)
	{
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_004b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		if (prefab == null || position == null || !index.TryGetValue(prefab, out var value))
		{
			return null;
		}
		Vector3 val = default(Vector3);
		((Vector3)(ref val))._002Ector(position[0], position[1], position[2]);
		BaseEntity result = null;
		float num = 1f;
		foreach (BaseEntity item in value)
		{
			Vector3 val2 = ((Component)item).transform.position - val;
			float sqrMagnitude = ((Vector3)(ref val2)).sqrMagnitude;
			if (sqrMagnitude <= num)
			{
				result = item;
				num = sqrMagnitude;
			}
		}
		return result;
	}

	private static void ApplySettings(BaseEntity entity, IOEntityInfo info)
	{
		if (!(entity is TimerSwitch timerSwitch))
		{
			if (!(entity is RFBroadcaster rFBroadcaster))
			{
				if (!(entity is RFReceiver rFReceiver))
				{
					if (!(entity is ElectricalBranch electricalBranch))
					{
						if (!(entity is PowerCounter powerCounter))
						{
							if (entity is CardReader cardReader && info.AccessLevel > 0)
							{
								cardReader.accessLevel = info.AccessLevel;
								cardReader.SetFlagLocal(cardReader.AccessLevel1, info.AccessLevel == 1);
								cardReader.SetFlagLocal(cardReader.AccessLevel2, info.AccessLevel == 2);
								cardReader.SetFlagLocal(cardReader.AccessLevel3, info.AccessLevel == 3);
							}
						}
						else
						{
							if (info.TargetCounterNumber > 0)
							{
								CounterTarget?.SetValue(powerCounter, info.TargetCounterNumber);
							}
							powerCounter.SetFlagLocal(BaseEntity.Flags.Reserved3, info.CounterPassthrough);
						}
					}
					else if (info.BranchAmount > 0)
					{
						electricalBranch.branchAmount = info.BranchAmount;
					}
				}
				else if (info.Frequency > 0)
				{
					rFReceiver.SetFrequency(info.Frequency);
				}
			}
			else if (info.Frequency > 0)
			{
				rFBroadcaster.SetFrequency(info.Frequency);
			}
		}
		else if (info.TimerLength > 0f)
		{
			timerSwitch.timerLength = info.TimerLength;
		}
		if (!string.IsNullOrEmpty(info.RcIdentifier))
		{
			AccessTools.Method(((object)entity).GetType(), "UpdateIdentifier", new Type[2]
			{
				typeof(string),
				typeof(bool)
			}, (Type[])null)?.Invoke(entity, new object[2] { info.RcIdentifier, false });
		}
		if (!string.IsNullOrEmpty(info.PhoneName))
		{
			PhoneController phoneController = ((object)entity).GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault((FieldInfo x) => x.FieldType == typeof(PhoneController))?.GetValue(entity) as PhoneController;
			if ((Object)(object)phoneController != (Object)null)
			{
				phoneController.PhoneName = info.PhoneName;
			}
		}
		if (info.PeaceKeeper)
		{
			AccessTools.Method(((object)entity).GetType(), "SetPeacekeepermode", new Type[1] { typeof(bool) }, (Type[])null)?.Invoke(entity, new object[1] { true });
		}
		entity.SendNetworkUpdate();
	}

	private static string Short(string prefab)
	{
		if (prefab != null)
		{
			return Path.GetFileNameWithoutExtension(prefab);
		}
		return "?";
	}

	private static string Format(float[] p)
	{
		if (p != null)
		{
			return $"{p[0]:0.0}, {p[1]:0.0}, {p[2]:0.0}";
		}
		return "?";
	}
}
