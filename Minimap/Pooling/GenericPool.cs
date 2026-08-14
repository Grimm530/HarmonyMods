using System.Collections.Generic;

namespace Oxide.Ext.Chaos.Pooling;

public class GenericPool<T> : GenericPool
{
}

public class GenericPool
{
	protected Dictionary<string, object> m_Collections = new Dictionary<string, object>();
}
