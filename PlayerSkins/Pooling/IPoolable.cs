namespace Ext.Chaos.Pooling;

public interface IPoolable
{
	void OnEnterPool();

	void OnLeavePool();
}
