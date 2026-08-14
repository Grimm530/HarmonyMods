using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace MinimapHarmony
{
    /// <summary>FileStorage-backed image cache replacing Oxide ImageLibrary.</summary>
    public static class ImageStore
    {
        private static readonly Dictionary<string, string> NameToCrc =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static string CacheDirectory =>
            Path.Combine(MinimapHost.Instance?.DataDirectory ?? ".", "cache");

        private static string IndexPath =>
            Path.Combine(MinimapHost.Instance?.DataDirectory ?? ".", "images.json");

        public static void LoadIndex()
        {
            NameToCrc.Clear();
            try
            {
                if (!File.Exists(IndexPath)) return;
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(IndexPath));
                if (loaded == null) return;
                foreach (var kv in loaded)
                {
                    if (!string.IsNullOrEmpty(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                        NameToCrc[kv.Key] = kv.Value;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] Image index load: " + ex.Message);
            }
        }

        public static void SaveIndex()
        {
            try
            {
                var dir = Path.GetDirectoryName(IndexPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(IndexPath, JsonConvert.SerializeObject(NameToCrc, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] Image index save: " + ex.Message);
            }
        }

        public static bool TryGetImage(string name, out string crc)
        {
            crc = null;
            if (string.IsNullOrEmpty(name)) return false;

            if (NameToCrc.TryGetValue(name, out var stored) && IsCrcLive(stored))
            {
                crc = stored;
                return true;
            }

            string pngPath = GetPngPath(name);
            if (File.Exists(pngPath))
            {
                crc = StoreBytes(name, File.ReadAllBytes(pngPath), persistIndex: true);
                return !string.IsNullOrEmpty(crc);
            }

            return false;
        }

        public static string AddImageData(string name, byte[] bytes, Action<string> callback = null)
        {
            string crc = StoreBytes(name, bytes, persistIndex: true);
            callback?.Invoke(crc);
            return crc;
        }

        public static void ImportPngFiles(string directory, string nameFormat, Action onComplete = null)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                onComplete?.Invoke();
                return;
            }

            foreach (var file in Directory.GetFiles(directory, "*.png"))
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                StoreBytes(fileName, File.ReadAllBytes(file), persistIndex: false);
            }
            SaveIndex();
            onComplete?.Invoke();
        }

        public static bool ImportEmbeddedArrows()
        {
            bool any = false;
            var asm = Assembly.GetExecutingAssembly();
            foreach (var resource in asm.GetManifestResourceNames())
            {
                int idx = resource.IndexOf("maparrow.", StringComparison.OrdinalIgnoreCase);
                if (idx < 0 || !resource.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    continue;
                string name = resource.Substring(idx, resource.Length - idx - 4);
                try
                {
                    using (var stream = asm.GetManifestResourceStream(resource))
                    {
                        if (stream == null) continue;
                        using (var ms = new MemoryStream())
                        {
                            stream.CopyTo(ms);
                            StoreBytes(name, ms.ToArray(), persistIndex: false);
                            any = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Minimap] Embedded arrow " + name + ": " + ex.Message);
                }
            }

            string images = MinimapHost.Instance?.ImagesDirectory;
            if (!string.IsNullOrEmpty(images) && Directory.Exists(images))
            {
                foreach (var file in Directory.GetFiles(images, "maparrow.*.png"))
                {
                    StoreBytes(Path.GetFileNameWithoutExtension(file), File.ReadAllBytes(file), persistIndex: false);
                    any = true;
                }
            }

            if (any) SaveIndex();
            return any;
        }

        private static string StoreBytes(string name, byte[] bytes, bool persistIndex)
        {
            if (string.IsNullOrEmpty(name) || bytes == null || bytes.Length == 0)
                return null;

            try
            {
                Directory.CreateDirectory(CacheDirectory);
                File.WriteAllBytes(GetPngPath(name), bytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] Cache write: " + ex.Message);
            }

            if (!FileStorageReady(out var ce))
                return null;

            try
            {
                uint crc = FileStorage.server.Store(bytes, FileStorage.Type.png, ce.net.ID);
                string crcStr = crc.ToString();
                NameToCrc[name] = crcStr;
                if (persistIndex) SaveIndex();
                return crcStr;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Minimap] FileStorage.Store " + name + ": " + ex.Message);
                return null;
            }
        }

        private static bool IsCrcLive(string crc)
        {
            if (string.IsNullOrEmpty(crc) || !uint.TryParse(crc, out var value) || value == 0)
                return false;
            if (!FileStorageReady(out var ce))
                return false;
            try
            {
                return FileStorage.server.Get(value, FileStorage.Type.png, ce.net.ID) != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool FileStorageReady(out CommunityEntity ce)
        {
            ce = null;
            try
            {
                var identity = ConVar.Server.identity;
                if (string.IsNullOrEmpty(identity) ||
                    string.Equals(identity, "my_server_identity", StringComparison.OrdinalIgnoreCase))
                    return false;
                ce = CommunityEntity.ServerInstance;
                return ce != null && ce.net != null && FileStorage.server != null;
            }
            catch
            {
                return false;
            }
        }

        private static string GetPngPath(string name)
        {
            string safe = SanitizeFileName(name);
            if (safe.Length > 80)
                safe = HashFileName(name);
            return Path.Combine(CacheDirectory, safe + ".png");
        }

        private static string SanitizeFileName(string name)
        {
            string safe = name ?? "image";
            foreach (var c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');
            safe = safe.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
            return safe;
        }

        private static string HashFileName(string name)
        {
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(name ?? ""));
                var sb = new System.Text.StringBuilder(40);
                for (int i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
