using System.Collections.Generic;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Ext.Chaos.Pooling;

namespace Oxide.Ext.Chaos.UIFramework;

public class UpdateComponent<T> : UpdateComponent where T : BaseCuiComponent
{
	public T Component;

	public override void Free()
	{
		UpdateComponent<T> t = this;
		UIUpdatePool.Free(ref t);
	}

	public override void OnEnterPool()
	{
		if (DirtyFields != null)
		{
			DirtyFields.Clear();
			Pool.FreeUnmanaged(ref DirtyFields);
		}
		Name = string.Empty;
		UIComponentPool.Free(ref Component);
	}

	public override void OnLeavePool()
	{
		Component = UIComponentPool.Get<T>();
	}

	public override void WriteJson(JsonWriter writer)
	{
		Component.WriteJson(writer, DirtyFields);
	}

	public override void Send(BasePlayer player)
	{
		ChaosUI.SendUpdate(player, this);
	}
}

public abstract class UpdateComponent : IPoolable
{
	public string Name;

	protected List<string> DirtyFields;

	public abstract void Send(BasePlayer player);

	public abstract void WriteJson(JsonWriter writer);

	public abstract void Free();

	public abstract void OnEnterPool();

	public abstract void OnLeavePool();

	public void MarkFieldsDirty(params string[] fieldNames)
	{
		if (DirtyFields == null)
			DirtyFields = Pool.Get<List<string>>();
		foreach (string item in fieldNames)
		{
			if (!DirtyFields.Contains(item))
				DirtyFields.Add(item);
		}
	}
}
