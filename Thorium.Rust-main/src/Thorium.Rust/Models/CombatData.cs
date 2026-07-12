namespace Thorium.Rust.Models;

public struct CombatData
{
    public bool IsAiming;
    public bool IsAttacking;
    public bool IsMounted;
    public BaseEntity LastTargetId;
    public float LastAttackTimeUnixMs;
    public string? Weapon;

    public static CombatData FromPlayer(BasePlayer player)
    {
        var activeItem = player.GetActiveItem();
        return new CombatData
        {
            IsAiming = player.IsAiming,
            IsAttacking = player.IsAttacking(),
            IsMounted = player.isMounted,
            LastTargetId = player.lastDealtDamageTo,
            LastAttackTimeUnixMs = player.lastDealtDamageTime,
            Weapon = activeItem?.info.shortname // Null instead of empty string to reduce allocations
        };
    }
}
