using System;
using Facepunch;

namespace Facepunch.Harmony.GatherManager
{
    public class OnPlayerConnectedArgs : Pool.IPooled
    {
        public BasePlayer Player { get; internal set; }

        public void EnterPool() { }

        public void LeavePool()
        {
            Player = null;
        }
    }
}
