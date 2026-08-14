// FileStorage-backed ImageLibrary stand-in (no Oxide ImageLibrary plugin).
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using Oxide.Core.Plugins;

namespace Oxide.Core.Plugins
{
    public sealed class FileStorageImageLibrary : Plugin
    {
        public static readonly FileStorageImageLibrary Instance = new FileStorageImageLibrary();

        private readonly Dictionary<string, string> _crc =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pending =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private FileStorageImageLibrary()
        {
            Name = "ImageLibrary";
            Title = "ImageLibrary";
            IsLoaded = true;
        }

        public override object Call(string hook, params object[] args)
        {
            if (string.IsNullOrEmpty(hook)) return null;
            switch (hook)
            {
                case "GetImage":
                    return GetImage(args != null && args.Length > 0 ? args[0]?.ToString() : null);
                case "HasImage":
                    return HasImage(args != null && args.Length > 0 ? args[0]?.ToString() : null);
                case "AddImage":
                    if (args == null || args.Length < 2) return false;
                    AddImage(args[0]?.ToString(), args[1]?.ToString());
                    return true;
                case "RemoveImage":
                    if (args != null && args.Length > 0 && args[0] != null)
                        _crc.Remove(args[0].ToString());
                    return true;
                case "ImportImageList":
                    ImportImageList(args);
                    return true;
                case "IsReady":
                    return _pending.Count == 0;
                default:
                    return null;
            }
        }

        public string GetImage(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            return _crc.TryGetValue(key, out var crc) ? crc : "";
        }

        public bool HasImage(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            return _crc.ContainsKey(key) && !string.IsNullOrEmpty(_crc[key]);
        }

        public void AddImage(string urlOrPath, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (string.IsNullOrEmpty(urlOrPath))
            {
                urlOrPath = key;
            }
            if (urlOrPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                StartDownload(key, urlOrPath);
                return;
            }
            TryStoreLocal(key, urlOrPath);
        }

        private void ImportImageList(object[] args)
        {
            Dictionary<string, string> images = null;
            Action callback = null;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] is Dictionary<string, string> d) images = d;
                    if (args[i] is Action a) callback = a;
                }
            }
            if (images != null)
            {
                foreach (var kv in images)
                    AddImage(kv.Value, kv.Key);
            }
            if (callback != null)
                Oxide.Core.Interface.NextTick(callback);
        }

        private void TryStoreLocal(string key, string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                StoreBytes(key, File.ReadAllBytes(path), Path.GetExtension(path));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ImageLibrary] local store " + key + ": " + ex.Message);
            }
        }

        private void StartDownload(string key, string url)
        {
            if (!_pending.Add(key)) return;
            try
            {
                if (ServerMgr.Instance != null)
                    ServerMgr.Instance.StartCoroutine(Download(key, url));
                else
                    Oxide.Core.Interface.NextTick(() =>
                    {
                        if (ServerMgr.Instance != null)
                            ServerMgr.Instance.StartCoroutine(Download(key, url));
                    });
            }
            catch (Exception ex)
            {
                _pending.Remove(key);
                Debug.LogWarning("[ImageLibrary] download start " + key + ": " + ex.Message);
            }
        }

        private IEnumerator Download(string key, string url)
        {
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();
                _pending.Remove(key);
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[ImageLibrary] download failed " + key + ": " + req.error);
                    yield break;
                }
                StoreBytes(key, req.downloadHandler?.data, ".png");
            }
        }

        private void StoreBytes(string key, byte[] bytes, string ext)
        {
            if (bytes == null || bytes.Length == 0) return;
            try
            {
                var owner = CommunityEntity.ServerInstance?.net?.ID ?? default(NetworkableId);
                if (owner == default(NetworkableId))
                {
                    Oxide.Core.Interface.NextTick(() => StoreBytes(key, bytes, ext));
                    return;
                }
                var type = (ext != null && ext.IndexOf("jpg", StringComparison.OrdinalIgnoreCase) >= 0)
                    ? FileStorage.Type.jpg
                    : FileStorage.Type.png;
                uint id = FileStorage.server.Store(bytes, type, owner);
                if (id != 0)
                    _crc[key] = id.ToString();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ImageLibrary] FileStorage.Store " + key + ": " + ex.Message);
            }
        }
    }
}
