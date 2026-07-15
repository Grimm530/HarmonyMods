/*
 * Harmony shims so the ported ItemRetriever 0.7.7 logic can run without Oxide/Carbon.
 * Slim library host - no config/data/commands/permissions.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ItemRetrieverHarmony
{
    #region Attributes

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class InfoAttribute : Attribute
    {
        public InfoAttribute(string title, string author, string version) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class PluginReferenceAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DescriptionAttribute : Attribute
    {
        public DescriptionAttribute(string description) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HookMethodAttribute : Attribute
    {
        public HookMethodAttribute(string name) { }
    }

    #endregion

    #region Plugin stub

    /// <summary>
    /// Oxide-compatible plugin identity. Call routes to the live ItemRetriever instance APIs.
    /// External mods (e.g. Backpacks) receive a bridge instance whose Call invokes ItemRetrieverHarmonyMod.
    /// </summary>
    public class Plugin
    {
        public string Name { get; set; } = "";
        public string Title { get; set; } = "";
        public bool IsLoaded { get; set; }

        /// <summary>Optional bound object for cross-assembly supplier identity (e.g. Backpacks instance).</summary>
        public object BoundInstance { get; set; }

        public virtual object Call(string method, params object[] args)
        {
            return ItemRetrieverHarmonyMod.CallApi(method, args);
        }
    }

    #endregion

    #region VersionNumber

    public struct VersionNumber : IComparable<VersionNumber>
    {
        public int Major;
        public int Minor;
        public int Patch;

        public VersionNumber(int major, int minor, int patch)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int CompareTo(VersionNumber other)
        {
            if (Major != other.Major) return Major.CompareTo(other.Major);
            if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
            return Patch.CompareTo(other.Patch);
        }

        public static bool operator >=(VersionNumber a, VersionNumber b) => a.CompareTo(b) >= 0;
        public static bool operator <=(VersionNumber a, VersionNumber b) => a.CompareTo(b) <= 0;
        public static bool operator >(VersionNumber a, VersionNumber b) => a.CompareTo(b) > 0;
        public static bool operator <(VersionNumber a, VersionNumber b) => a.CompareTo(b) < 0;
        public static bool operator ==(VersionNumber a, VersionNumber b) => a.CompareTo(b) == 0;
        public static bool operator !=(VersionNumber a, VersionNumber b) => a.CompareTo(b) != 0;

        public override bool Equals(object obj) => obj is VersionNumber other && this == other;
        public override int GetHashCode() => Major * 397 ^ Minor * 31 ^ Patch;
        public override string ToString() => $"{Major}.{Minor}.{Patch}";
    }

    #endregion

    #region Interface / Oxide stub

    public class OxideStub
    {
        public object CallHook(string name, params object[] args) => Interface.CallHook(name, args);
        public void NextTick(Action action) => Interface.NextTick(action);
    }

    public static class Interface
    {
        public static OxideStub Oxide { get; } = new OxideStub();

        /// <summary>No-op under Harmony unless another mod registers a hook bridge.</summary>
        public static object CallHook(string name, params object[] args) => null;

        public static void NextTick(Action action)
        {
            try { ServerMgr.Instance?.StartCoroutine(NextTickCoroutine(action)); }
            catch { }
        }

        private static IEnumerator NextTickCoroutine(Action action)
        {
            yield return null;
            try { action?.Invoke(); }
            catch (Exception ex) { Debug.LogWarning("[ItemRetriever] NextTick: " + ex.Message); }
        }
    }

    #endregion

    #region ItemContainer event helpers (publicizer field/event ambiguity)

    public static class ItemContainerHooks
    {
        private static readonly FieldInfo OnDirtyField =
            typeof(ItemContainer).GetField("onDirty", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static void AddOnDirty(ItemContainer container, Action callback)
        {
            if (container == null || callback == null || OnDirtyField == null) return;
            var current = OnDirtyField.GetValue(container) as Action;
            OnDirtyField.SetValue(container, current + callback);
        }

        public static void RemoveOnDirty(ItemContainer container, Action callback)
        {
            if (container == null || callback == null || OnDirtyField == null) return;
            var current = OnDirtyField.GetValue(container) as Action;
            OnDirtyField.SetValue(container, current - callback);
        }
    }

    #endregion

    #region Host

    public class ItemRetrieverHost
    {
        public static ItemRetrieverHost Instance { get; private set; }
        public ItemRetriever Plugin { get; set; }
        public string ServerRoot { get; private set; }

        public static void Init(string serverRoot)
        {
            Instance = new ItemRetrieverHost { ServerRoot = serverRoot };
        }

        public static void Shutdown()
        {
            Instance = null;
        }

        public void Puts(string message) => Debug.Log("[ItemRetriever] " + message);
        public void PrintWarning(string message) => Debug.LogWarning("[ItemRetriever] " + message);
        public void PrintError(string message) => Debug.LogError("[ItemRetriever] " + message);
    }

    #endregion

    #region PluginBase

    public abstract class ItemRetrieverPluginBase
    {
        public string Name => "ItemRetriever";
        public string Title => "Item Retriever";
        public VersionNumber Version { get; protected set; } = new VersionNumber(0, 7, 7);
        public bool IsLoaded { get; set; } = true;

        protected ItemRetrieverHost Host => ItemRetrieverHost.Instance;

        protected void Puts(string message) => Host?.Puts(message);
        protected void PrintWarning(string message) => Host?.PrintWarning(message);
        protected void PrintError(string message) => Host?.PrintError(message);

        protected void NextTick(Action action) => Interface.NextTick(action);

        public abstract void HarmonyInit();
        public abstract void HarmonyServerInitialized();
        public abstract void HarmonyUnload();
    }

    #endregion
}
