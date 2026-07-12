namespace GrimmNPC2
{
    /// <summary>
    /// Stable string tokens for <see cref="Rust.Ai.Gen2.FSMStateBase.Name"/> comparisons (GEN2 strips the
    /// <c>State_</c> prefix from the type name by default). Use for debug/UI/plugins; stock transitions
    /// remain authoritative.
    /// </summary>
    public static class Gen2ScientistStateNames
    {
        public const string PatrolIdle = "PatrolIdle";
        public const string Patrol = "Patrol";
        public const string Search = "Search";
        public const string ScientistRush = "ScientistRush";
        public const string ScientistDead = "ScientistDead";
        public const string Dead = "Dead";
        public const string DogFight = "DogFight";
        public const string MoveToCoverHiddenFromTarget = "MoveToCoverHiddenFromTarget";
        public const string MoveToPointWithLosOnTarget = "MoveToPointWithLosOnTarget";
        public const string StayInCover = "StayInCover";
        public const string ScientistSurprised = "ScientistSurprised";
        public const string Flank = "Flank";
        public const string ThrowGrenade = "ThrowGrenade";
        public const string ScriptedNade = "ScriptedNade";
        public const string Nothing = "Nothing";
    }

    /// <summary>
    /// Which stock humanoid GEN2 FSM graph is present on the entity. Determined by component type
    /// (<see cref="Rust.Ai.Gen2.Scientist2FSM"/>, <see cref="Rust.Ai.Gen2.Scientist2FSM_Heavy"/>,
    /// <see cref="Rust.Ai.Gen2.Scientist2FSM_Shotgun"/>). Predators/croc use other FSMs; not listed here.
    /// </summary>
    public enum ScientistGen2FsmKind
    {
        Unknown = 0,
        DefaultScientist = 1,
        Heavy = 2,
        Shotgun = 3
    }
}
