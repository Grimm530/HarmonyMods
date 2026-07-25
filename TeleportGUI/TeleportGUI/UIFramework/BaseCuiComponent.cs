using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Ext.Chaos.Pooling;

namespace Oxide.Ext.Chaos.UIFramework;

public abstract class BaseCuiComponent : IPoolable
{
	public bool IsConstant { get; protected set; }

	public abstract void CopyFrom<T>(T other) where T : BaseCuiComponent;

	public abstract void WriteJson(JsonWriter jsonWriter, List<string> dirtyFields);

	protected bool IsFieldDirty(string fieldName, List<string> dirtyFields)
	{
		return dirtyFields?.Contains(fieldName) ?? false;
	}

	public virtual void OnEnterPool()
	{
	}

	void IPoolable.OnEnterPool()
	{
		//ILSpy generated this explicit interface implementation from .override directive in OnEnterPool
		this.OnEnterPool();
	}

	public virtual void OnLeavePool()
	{
	}

	void IPoolable.OnLeavePool()
	{
		//ILSpy generated this explicit interface implementation from .override directive in OnLeavePool
		this.OnLeavePool();
	}
}
