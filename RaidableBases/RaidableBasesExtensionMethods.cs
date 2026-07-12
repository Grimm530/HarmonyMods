using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Facepunch;
using Network;
using Newtonsoft.Json;
using Rust;
using UnityEngine;
using IPlayer = RaidableBases.IPlayer;

namespace RaidableBases.RaidableBasesExtensionMethods
{
    public static class ExtensionMethods
    {
        public class DisposableBuilder : IDisposable, Pool.IPooled
        {
            private StringBuilder _builder;
            public DisposableBuilder() { }
            public void LeavePool() => _builder = Pool.Get<StringBuilder>();
            public void EnterPool() => Pool.FreeUnmanaged(ref _builder);
            public void Dispose() { DisposableBuilder obj = this; Pool.Free(ref obj); }
            public static DisposableBuilder Get() => Pool.Get<DisposableBuilder>();
            public DisposableBuilder Append(DisposableBuilder obj) { _builder.Append(obj._builder); return this; }
            public DisposableBuilder Append(string value) { _builder.Append(value); return this; }
            public DisposableBuilder AppendLine(string value = null) { if (value != null) _builder.AppendLine(value); else _builder.AppendLine(); return this; }
            public DisposableBuilder Replace(string oldValue, string newValue) { _builder.Replace(oldValue, newValue); return this; }
            public DisposableBuilder Clear() { _builder.Clear(); return this; }
            public override string ToString() => _builder.ToString();
            public int Length { get => _builder.Length; set => _builder.Length = value; }
        }
        public static string[] ToStringArray(this string[] args) => args;
        public static string[] ToStringArray(this StringView[] args) { if (args == null || args.Length == 0) return Array.Empty<string>(); string[] array = new string[args.Length]; for (int i = 0; i < args.Length; i++) array[i] = args[i].ToString(); return array; } 
        public static string ToFriendlyJson(this string s) => string.IsNullOrEmpty(s) ? s : Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        public static string FromFriendlyJson(this string s) => string.IsNullOrEmpty(s) ? s : Encoding.UTF8.GetString(Convert.FromBase64String((s.Replace('-', '+').Replace('_', '/')).PadRight(s.Length + (4 - s.Length % 4) % 4, '=')));
        public static PooledList<T> ToPooledList<T>(this IEnumerable<T> a) { var b = Facepunch.Pool.Get<PooledList<T>>(); if (a != null) b.AddRange(a); return b; }
        public static PooledList<T> TakePooledList<T>(this IEnumerable<T> a, int n) { var b = Facepunch.Pool.Get<PooledList<T>>(); if (a != null) { foreach (var d in a) { b.Add(d); if (b.Count >= n) { break; } } } return b; }
        public static PooledList<Item> GetAllItems(this BasePlayer a) { var b = Facepunch.Pool.Get<PooledList<Item>>(); if (a != null && a.inventory != null) { a.inventory.GetAllItems(b); } return b; }
        public static KeyValuePair<K, V> GetRandom<K, V>(this IDictionary<K, V> a) => a == null || a.Count == 0 ? default : a.ElementAt(UnityEngine.Random.Range(0, a.Count));
        public static bool All<T>(this IEnumerable<T> a, Func<T, bool> b) { foreach (T c in a) { if (!b(c)) { return false; } } return true; }
        public static int Average(this IList<int> a) { if (a.Count == 0) { return 0; } int b = 0; for (int i = 0; i < a.Count; i++) { b += a[i]; } return b != 0 ? b / a.Count : 0; }
        public static T ElementAt<T>(this IEnumerable<T> a, int b) { if (a is IList<T> c) { return c[b]; } using IEnumerator<T> d = a.GetEnumerator(); while (d.MoveNext()) { if (b == 0) { return d.Current; } b--; } return default; }
        public static bool Exists<T>(this HashSet<T> a) where T : BaseEntity { foreach (var b in a) { if (!b.IsKilled()) { return true; } } return false; }
        public static bool Exists<T>(this IEnumerable<T> a, Func<T, bool> b = null) { using var c = a.GetEnumerator(); while (c.MoveNext()) { if (b == null || b(c.Current)) { return true; } } return false; }
        public static T FirstOrDefault<T>(this IEnumerable<T> a, Func<T, bool> b = null) { using (var c = a.GetEnumerator()) { while (c.MoveNext()) { if (b == null || b(c.Current)) { return c.Current; } } } return default; }
        public static void ForEach<T>(this IEnumerable<T> a, Action<T> action) { foreach (T n in a) { action(n); } }
        public static int RemoveAll<TKey, TValue>(this IDictionary<TKey, TValue> c, Func<TKey, TValue, bool> d) { int a = 0; if (c.IsNullOrEmpty()) return a; using var e = c.ToPooledList(); foreach (var b in e) { if (d(b.Key, b.Value)) { c.Remove(b.Key); a++; } } return a; }
        public static IEnumerable<V> Select<T, V>(this IEnumerable<T> a, Func<T, V> b) { var c = new List<V>(); using (var d = a.GetEnumerator()) { while (d.MoveNext()) { c.Add(b(d.Current)); } } return c; }
        public static string[] Skip(this string[] a, int b) { if (a.Length == 0 || b >= a.Length) { return Array.Empty<string>(); } int n = a.Length - b; string[] c = new string[n]; Array.Copy(a, b, c, 0, n); return c; }
        public static Dictionary<T, V> ToDictionary<S, T, V>(this IEnumerable<S> a, Func<S, T> b, Func<S, V> c) { var d = new Dictionary<T, V>(); using (var e = a.GetEnumerator()) { while (e.MoveNext()) { d[b(e.Current)] = c(e.Current); } } return d; }
        public static List<T> ToList<T>(this IEnumerable<T> a) => new(a);
        public static List<T> Where<T>(this IEnumerable<T> a, Func<T, bool> b) { List<T> c = new(a is ICollection<T> n ? n.Count : 4); foreach (var d in a) { if (b(d)) { c.Add(d); } } return c; }
        public static List<T> OrderByAscending<T, TKey>(this IEnumerable<T> a, Func<T, TKey> s) { List<T> m = new(a); m.Sort((x, y) => Comparer<TKey>.Default.Compare(s(x), s(y))); return m; }
        public static int Sum<T>(this IEnumerable<T> a, Func<T, int> b) { int c = 0; foreach (T d in a) { c += b(d); } return c; }
        public static int Count<T>(this IEnumerable<T> a, Func<T, bool> b = null) { int c = 0; foreach (T d in a) { if (b == null || b(d)) { c++; } } return c; }
        public static IEnumerable<T> Union<T>(this IEnumerable<T> a, IEnumerable<T> b, IEqualityComparer<T> c = null) { HashSet<T> d = new(c); foreach (T e in a) { if (d.Add(e)) { yield return e; } } foreach (T f in b) { if (d.Add(f)) { yield return f; } } }
        public static bool HasPermission(this string a, string b) { var p = global::RaidableBases.RaidableBasesHost.Instance?.Permission; return p != null && !string.IsNullOrEmpty(a) && p.UserHasPermission(a, b); }
        public static bool HasPermission(this BasePlayer a, string b) => a != null && a.UserIDString.HasPermission(b);
        public static bool IsSteamId(this ulong id) => id >= 76561197960265728UL;
        public static bool HasPermission(this ulong a, string b) => a.IsSteamId() && a.ToString().HasPermission(b);
        public static bool BelongsToGroup(this string a, string b) { var p = global::RaidableBases.RaidableBasesHost.Instance?.Permission; return p != null && !string.IsNullOrEmpty(a) && p.UserHasGroup(a, b); }
        public static bool BelongsToGroup(this ulong a, string b) => a.ToString().BelongsToGroup(b);
        public static bool BelongsToGroup(this BasePlayer a, string b) => a != null && a.UserIDString.BelongsToGroup(b);
        public static bool IsOnline(this BasePlayer a) => a.IsNetworked() && a.net.connection != null;
        public static bool IsKilled(this BaseNetworkable a) => a == null || a.IsDestroyed || !a.isSpawned;
        public static bool IsNull(this BaseNetworkable a) => a == null || a.IsDestroyed;
        public static bool IsNullOrEmpty<T>(this IReadOnlyCollection<T> c) => c == null || c.Count == 0;
        public static bool IsNetworked(this BaseNetworkable a) => !(a == null || a.IsDestroyed || !a.isSpawned || a.net == null);
        public static void SafelyKill(this BaseNetworkable a) { try { if (!a.IsKilled()) a.Kill(BaseNetworkable.DestroyMode.None); } catch { } }
        public static void DelayedSafeKill(this BaseNetworkable a) { if (!a.IsKilled()) a.Invoke(a.SafelyKill, 0.0625f); }
        public static bool CanCall(this object o) => o != null;
        public static bool IsHuman(this BasePlayer a)
        {
            if (a == null) return false;
            // userID is EncryptedValue<ulong> — must cast; object.IsSteamId looks for .Value and always fails.
            try { return ((ulong)a.userID).IsSteamId(); }
            catch { return a.UserIDString.IsSteamId(); }
        }
        public static bool IsCheating(this BasePlayer a) => a._limitedNetworking || a.IsFlying || a.UsedAdminCheat(30f) || a.IsGod() || a.metabolism?.calories?.min == 500;
        public static void SetAiming(this BasePlayer a, bool f) { a.modelState.aiming = f; a.SendNetworkUpdate(); }
        public static void SetNoTarget(this AutoTurret a) { if (a == null) return; a.SetTarget(null); a.target = null; }
        public static void SafelyStrip(this PlayerInventory inv) { if (inv == null) return; inv.containerMain?.Clear(); inv.containerWear?.Clear(); inv.containerBelt?.Clear(); ItemManager.DoRemoves(); }
        public static void SafelyRemove(this ItemContainer inv, string shortname) { if (inv == null) return; Item item = inv.FindItemByItemName(shortname); if (item == null) return; item.RemoveFromContainer(); item.Remove(); }
        public static BasePlayer Player(this IPlayer user) => user?.Object as BasePlayer;
        public static string MaterialName(this Collider collider) { try { return collider.sharedMaterial.name; } catch { return string.Empty; } }
        public static string ObjectName(this Collider collider) { try { return collider.name ?? string.Empty; } catch { return string.Empty; } }
        public static Vector3 GetPosition(this Collider collider) { try { return collider.transform.position; } catch { return Vector3.zero; } }
        public static string ObjectName(this BaseEntity entity) { try { return entity.name; } catch { return string.Empty; } }
        public static T GetRandom<T>(this HashSet<T> h) { if (h == null || h.Count == 0) { return default; } return h.ElementAt(UnityEngine.Random.Range(0, h.Count)); }
        public static float Distance(this Vector3 a, Vector3 b) => (a - b).magnitude;
        public static float Distance2D(this Vector3 a, Vector3 b) => (a.XZ2D() - b.XZ2D()).magnitude;
        public static void ResetToPool<K, V>(this Dictionary<K, V> obj) { if (obj == null) return; obj.Clear(); Pool.FreeUnmanaged(ref obj); }
        public static void ResetToPool<T>(this HashSet<T> obj) { if (obj == null) return; obj.Clear(); Pool.FreeUnmanaged(ref obj); }
        public static void ResetToPool<T>(this List<T> obj) { if (obj == null) return; obj.Clear(); Pool.FreeUnmanaged(ref obj); }
        public static void ResetToPool<T>(this T obj) where T : class, Pool.IPooled, new() { if (obj != null) Pool.Free(ref obj); }
        public static ulong userid(this BasePlayer player) => (ulong)player.userID;

        public static bool HasPermission(this IPlayer p, string perm) => p != null && !string.IsNullOrEmpty(p.Id) && p.Id.HasPermission(perm);
        public static bool IsSteamId(this string id) => !string.IsNullOrEmpty(id) && id.Length >= 17 && ulong.TryParse(id, out var v) && v.IsSteamId();
        public static bool IsSteamId(this object o)
        {
            if (o == null) return false;
            if (o is ulong u) return u.IsSteamId();
            if (o is string s) return s.IsSteamId();
            try
            {
                var t = o.GetType();
                // EncryptedValue<ulong> exposes Get(), not Value.
                var get = t.GetMethod("Get", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, null, System.Type.EmptyTypes, null);
                if (get != null)
                {
                    var v = get.Invoke(o, null);
                    if (v is ulong uGet) return uGet.IsSteamId();
                }
                var prop = t.GetProperty("Value", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null) { var v = prop.GetValue(o); if (v is ulong u2) return u2.IsSteamId(); }
            }
            catch { }
            return false;
        }
        public static IPlayer GetIPlayer(this BasePlayer p) => p == null ? null : new global::RaidableBases.BasePlayerWrapper(p);
        public static TValue GetValueOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default) => dict != null && dict.TryGetValue(key, out var v) ? v : defaultValue;
        public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value) { key = pair.Key; value = pair.Value; }
        public static string SentenceCase(this string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + (s.Length > 1 ? s.Substring(1).ToLowerInvariant() : "");
        public static string TitleCase(this string s) => string.IsNullOrEmpty(s) ? s : string.Join(" ", s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w.Substring(1).ToLowerInvariant() : "") : ""));
    }
}
