using BaseEntityFlags = BaseEntity.Flags;

/// <summary>
/// Restores BaseEntity.SetFlag(Flags, bool, bool, bool) at compile time via StartSetFlags / FlagsUpdateScope.
/// </summary>
internal static class BaseEntityFlagCompat
{
    public static void SetFlag(this BaseEntity entity, BaseEntityFlags f, bool b, bool recursive = false, bool networkupdate = true)
    {
        if (entity == null || entity.IsDestroyed)
            return;

        BaseEntity.FlagsUpdateMode mode = networkupdate
            ? BaseEntity.FlagsUpdateMode.SendNetworkUpdate
            : BaseEntity.FlagsUpdateMode.Local;

        using BaseEntity.FlagsUpdateScope scope = entity.StartSetFlags(mode);
        scope.Set(f, b, recursive);
    }
}
