namespace Facepunch.Harmony.GatherManager
{
    public enum GatherSource
    {
        Dispenser,
        Growable,
        Pickup,
        Quarry,
        Excavator,
        Survey,
        Loot
    }

    public class OnGatherItemArgs : Pool.IPooled
    {
        public BasePlayer Player { get; internal set; }

        public BaseEntity Entity { get; internal set; }

        public Item GivenItem { get; internal set; }

        public bool IsFinishingBonus { get; internal set; }

        public ResourceDispenser ResourceDispenser { get; internal set; }

        public GatherSource Source { get; internal set; }

        public bool Cancel { get; internal set; }

        public void EnterPool()
        {
            
        }

        public void LeavePool()
        {
            Player = null;
            Entity = null;
            GivenItem = null;
            ResourceDispenser = null;
            Cancel = false;
        }
    }
}
