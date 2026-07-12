using System;
using System.Collections.Generic;
using UnityEngine;

namespace TruePVE;

public static class LootDefenderState
{
	public enum LockType
	{
		Bradley,
		Heli,
		NPC,
		Hackable
	}

	public class DamageEntry
	{
		public float Damage;

		public DateTime When;

		public ulong TeamId;

		public ulong UserId;

		public string Weapon;
	}

	public class LockInfo
	{
		public LockType Type;

		public HashSet<ulong> OwnerUserIds = new HashSet<ulong>();

		public DateTime ExpiresAt;

		public Vector3 Position;

		public float Radius;

		public ulong VictimNetId;
	}

	private static readonly Dictionary<ulong, List<DamageEntry>> DamageByEntity = new Dictionary<ulong, List<DamageEntry>>();

	private static readonly object Lock = new object();

	private static readonly Dictionary<ulong, LockInfo> LocksByEntity = new Dictionary<ulong, LockInfo>();

	private static readonly List<LockInfo> PositionLocks = new List<LockInfo>();

	public static void RecordDamage(ulong victimNetId, BasePlayer attacker, float amount, string weapon = "")
	{
		if ((Object)(object)attacker == (Object)null || amount <= 0f)
		{
			return;
		}
		lock (Lock)
		{
			if (!DamageByEntity.TryGetValue(victimNetId, out var value))
			{
				value = new List<DamageEntry>();
				DamageByEntity[victimNetId] = value;
			}
			value.Add(new DamageEntry
			{
				Damage = amount,
				When = DateTime.UtcNow,
				TeamId = attacker.currentTeam,
				UserId = attacker.userID,
				Weapon = (weapon ?? "")
			});
		}
	}

	public static void ApplyLock(ulong victimNetId, BaseCombatEntity victim, LockType type, int lockSeconds, float radius)
	{
		ApplyPositionLock(victim, type, lockSeconds, radius);
	}

	public static void ApplyPositionLock(BaseCombatEntity victim, LockType type, int lockSeconds, float radius)
	{
		//IL_02b9: Unknown result type (might be due to invalid IL or missing references)
		//IL_02ac: Unknown result type (might be due to invalid IL or missing references)
		//IL_02be: Unknown result type (might be due to invalid IL or missing references)
		if (TruePVEMod.Instance?.Config?.LootDefender == null || !TruePVEMod.Instance.Config.LootDefender.Enabled)
		{
			return;
		}
		lock (Lock)
		{
			ulong value = victim.net.ID.Value;
			if (!DamageByEntity.TryGetValue(value, out var value2) || value2.Count == 0)
			{
				return;
			}
			bool groupByTeam = TruePVEMod.Instance.Config.LootDefender.GroupByTeam;
			Dictionary<ulong, float> dictionary = new Dictionary<ulong, float>();
			foreach (DamageEntry item in value2)
			{
				ulong key = ((groupByTeam && item.TeamId != 0L) ? item.TeamId : item.UserId);
				if (!dictionary.TryGetValue(key, out var value3))
				{
					value3 = 0f;
				}
				dictionary[key] = value3 + item.Damage;
			}
			float num = 0f;
			foreach (float value4 in dictionary.Values)
			{
				num += value4;
			}
			if (num <= 0f)
			{
				return;
			}
			float num2 = type switch
			{
				LockType.Heli => TruePVEMod.Instance.Config.LootDefender.HeliThreshold, 
				LockType.Bradley => TruePVEMod.Instance.Config.LootDefender.BradleyThreshold, 
				_ => TruePVEMod.Instance.Config.LootDefender.NpcThreshold, 
			};
			HashSet<ulong> hashSet = new HashSet<ulong>();
			foreach (KeyValuePair<ulong, float> item2 in dictionary)
			{
				if (item2.Value / num < num2)
				{
					continue;
				}
				if (groupByTeam && item2.Key != 0L && (Object)(object)RelationshipManager.ServerInstance != (Object)null)
				{
					RelationshipManager.PlayerTeam playerTeam = RelationshipManager.ServerInstance.FindTeam(item2.Key);
					if (playerTeam?.members != null)
					{
						foreach (ulong member in playerTeam.members)
						{
							hashSet.Add(member);
						}
					}
					else
					{
						hashSet.Add(item2.Key);
					}
				}
				else
				{
					hashSet.Add(item2.Key);
				}
			}
			if (hashSet.Count != 0)
			{
				LockInfo lockInfo = new LockInfo
				{
					Type = type,
					OwnerUserIds = hashSet,
					Position = (((Object)(object)victim != (Object)null) ? ((Component)victim).transform.position : Vector3.zero),
					Radius = radius,
					ExpiresAt = ((lockSeconds > 0) ? DateTime.UtcNow.AddSeconds(lockSeconds) : DateTime.MaxValue)
				};
				PositionLocks.Add(lockInfo);
				if (victim?.net != null)
				{
					LocksByEntity[value] = lockInfo;
					DamageByEntity.Remove(value);
				}
			}
		}
	}

	public static bool IsPositionLocked(Vector3 worldPosition, ulong looterUserId, out bool isOwnerOrAlly)
	{
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0090: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Unknown result type (might be due to invalid IL or missing references)
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		isOwnerOrAlly = false;
		if (TruePVEMod.Instance?.Config?.LootDefender == null || !TruePVEMod.Instance.Config.LootDefender.Enabled)
		{
			return false;
		}
		lock (Lock)
		{
			for (int num = PositionLocks.Count - 1; num >= 0; num--)
			{
				LockInfo lockInfo = PositionLocks[num];
				if (DateTime.UtcNow > lockInfo.ExpiresAt)
				{
					PositionLocks.RemoveAt(num);
				}
				else
				{
					Vector3 val = lockInfo.Position - worldPosition;
					if (val.sqrMagnitude <= lockInfo.Radius * lockInfo.Radius)
					{
						if (lockInfo.OwnerUserIds.Contains(looterUserId))
						{
							isOwnerOrAlly = true;
							return false;
						}
						if (TruePVEMod.Instance.Config.LootDefender.AllowAllies)
						{
							foreach (ulong ownerUserId in lockInfo.OwnerUserIds)
							{
								if (TruePVEMod.Instance.IsAlly(ownerUserId, looterUserId))
								{
									isOwnerOrAlly = true;
									return false;
								}
							}
						}
						return true;
					}
				}
			}
		}
		return false;
	}

	public static bool IsEntityLocked(ulong entityNetId, ulong looterUserId, out bool isOwnerOrAlly)
	{
		isOwnerOrAlly = false;
		if (TruePVEMod.Instance?.Config?.LootDefender == null || !TruePVEMod.Instance.Config.LootDefender.Enabled)
		{
			return false;
		}
		lock (Lock)
		{
			if (!LocksByEntity.TryGetValue(entityNetId, out var value))
			{
				return false;
			}
			if (DateTime.UtcNow > value.ExpiresAt)
			{
				LocksByEntity.Remove(entityNetId);
				return false;
			}
			if (value.OwnerUserIds.Contains(looterUserId))
			{
				isOwnerOrAlly = true;
				return false;
			}
			if (TruePVEMod.Instance.Config.LootDefender.AllowAllies)
			{
				foreach (ulong ownerUserId in value.OwnerUserIds)
				{
					if (TruePVEMod.Instance.IsAlly(ownerUserId, looterUserId))
					{
						isOwnerOrAlly = true;
						return false;
					}
				}
			}
			return true;
		}
	}

	public static void Clear()
	{
		lock (Lock)
		{
			DamageByEntity.Clear();
			LocksByEntity.Clear();
			PositionLocks.Clear();
		}
	}
}
