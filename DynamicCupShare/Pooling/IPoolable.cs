namespace Oxide.Ext.Chaos.Pooling;

public interface IPoolable
{
	void OnEnterPool();

	void OnLeavePool();
}
