// Shared bridge: register BaseCombatEntity.Hurt handlers with 0GrimmCore's unified dispatcher.
// Link this file into Harmony mods (see 0GrimmCore README). No reference to 0GrimmCore.dll required.

using System;

/// <summary>
/// Prefix handler for the unified Hurt dispatcher.
/// null = no opinion (continue chain); true = block damage (skip Hurt); false = unused.
/// </summary>
public delegate bool? GrimmCoreHurtPrefixHandler(BaseCombatEntity entity, HitInfo info);

/// <summary>Side-effect prefix: always runs, never blocks. Runs before block handlers.</summary>
public delegate void GrimmCoreHurtSideEffectHandler(BaseCombatEntity entity, HitInfo info);

/// <summary>Postfix: runs after Hurt when damage was not blocked at prefix stage.</summary>
public delegate void GrimmCoreHurtPostfixHandler(BaseCombatEntity entity, HitInfo info);

/// <summary>
/// Harmony hook semantics helper: non-null hook return = cancel damage.
/// </summary>
public static class GrimmCoreHurtSemantics
{
    public static bool? BlockIfHandled(object hookResult) => hookResult == null ? (bool?)null : true;
}

public static class GrimmCoreBridge
{
    public const string CustomEntitySkinIdKey = "GrimmCore_CustomEntitySkinId";
    public const string RegisterPrefixKey = "GrimmCore_RegisterHurtPrefix";
    public const string RegisterSideEffectKey = "GrimmCore_RegisterHurtSideEffect";
    public const string RegisterPostfixKey = "GrimmCore_RegisterHurtPostfix";
    public const string UnregisterModKey = "GrimmCore_UnregisterHurtMod";

    /// <summary>Blank Steam workshop skin marking custom/mod entities (nivex 3793751435).</summary>
    public const ulong CustomEntitySkinId = 3793751435UL;

    /// <summary>Legacy GrimmNPC scientist marker (invalid Steam id — existing saves).</summary>
    public const ulong LegacyGrimmNpcSkinId = 11162132011012UL;

    /// <summary>RaidableBases raid entity marker.</summary>
    public const ulong LegacyRaidableBasesSkinId = 3710562502UL;

    /// <summary>Legacy AnimalSpawn custom animal marker.</summary>
    public const ulong LegacyAnimalSpawnSkinId = 11491311214163UL;

    public static bool IsMarkedCustomEntity(BaseEntity entity)
    {
        if (entity == null) return false;
        ulong skin = entity.skinID;
        return skin == GetCustomEntitySkinId()
            || skin == LegacyGrimmNpcSkinId
            || skin == LegacyRaidableBasesSkinId
            || skin == LegacyAnimalSpawnSkinId;
    }

    public static bool IsMarkedCustomNpc(BasePlayer player)
        => player != null && player.IsNpc && IsMarkedCustomEntity(player);

    public static ulong GetCustomEntitySkinId()
    {
        try
        {
            if (AppDomain.CurrentDomain.GetData(CustomEntitySkinIdKey) is ulong u)
                return u;
        }
        catch { }
        return CustomEntitySkinId;
    }

    public static bool RegisterHurtPrefix(string modId, int priority, GrimmCoreHurtPrefixHandler handler)
    {
        if (string.IsNullOrEmpty(modId) || handler == null) return false;
        try
        {
            var fn = AppDomain.CurrentDomain.GetData(RegisterPrefixKey) as Action<string, int, Delegate>;
            fn?.Invoke(modId, priority, handler);
            return fn != null;
        }
        catch { return false; }
    }

    public static bool RegisterHurtSideEffect(string modId, int priority, GrimmCoreHurtSideEffectHandler handler)
    {
        if (string.IsNullOrEmpty(modId) || handler == null) return false;
        try
        {
            var fn = AppDomain.CurrentDomain.GetData(RegisterSideEffectKey) as Action<string, int, Delegate>;
            fn?.Invoke(modId, priority, handler);
            return fn != null;
        }
        catch { return false; }
    }

    public static bool RegisterHurtPostfix(string modId, int priority, GrimmCoreHurtPostfixHandler handler)
    {
        if (string.IsNullOrEmpty(modId) || handler == null) return false;
        try
        {
            var fn = AppDomain.CurrentDomain.GetData(RegisterPostfixKey) as Action<string, int, Delegate>;
            fn?.Invoke(modId, priority, handler);
            return fn != null;
        }
        catch { return false; }
    }

    public static void UnregisterHurtMod(string modId)
    {
        if (string.IsNullOrEmpty(modId)) return;
        try
        {
            var fn = AppDomain.CurrentDomain.GetData(UnregisterModKey) as Action<string>;
            fn?.Invoke(modId);
        }
        catch { }
    }

    /// <summary>Tag a spawned entity as mod-owned (AI + identification).</summary>
    public static void TagCustomEntity(BaseEntity entity)
    {
        if (entity == null) return;
        entity.skinID = GetCustomEntitySkinId();
    }
}
