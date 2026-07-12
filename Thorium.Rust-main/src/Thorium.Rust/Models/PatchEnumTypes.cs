namespace Thorium.Rust.Models;

public enum SnapshotTypeEnums
{
    Unknown = 0,
    PlayerTick,
    Join,
    Leave,
    Hurt,
    HurtEnv,
    Die,
    MoveItem,
    EntityKill,
    StashExposed,
    StashBuried,
    StashOpened,
    StashBuiltOver
}