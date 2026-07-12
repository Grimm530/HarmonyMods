using System;
using System.IO;
using ConVar;
using UnityEngine;

namespace Thorium.Rust.Config;

public class ThoriumConfig
{
    public string ServerToken { get; set; }
    public bool Debug { get; set; }
}

public static class ThoriumConfigService
{
    private const string CONFIG_FOLDER = "../../.thorium";
    private const string CONFIG_FILE = "thorium.yml";

    private static ThoriumConfig _config = new();
    private static string _configPath = string.Empty;
    private static bool _isLoaded;

    public static ThoriumConfig Config => _config;
    public static bool IsLoaded => _isLoaded;
    public static bool HasValidToken => !string.IsNullOrWhiteSpace(_config.ServerToken);
    public static string ServerToken => _config.ServerToken;
    public static bool DebugMode => _config.Debug;

    public static void Initialize()
    {
        InitializeConfigPath();
        LoadConfig();
    }

    public static void Reset()
    {
        _config = new ThoriumConfig();
        _configPath = string.Empty;
        _isLoaded = false;
    }

    public static bool SetServerToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        _config.ServerToken = token.Trim();
        return SaveConfig();
    }

    public static bool SetDebugMode(bool enabled)
    {
        _config.Debug = enabled;
        return SaveConfig();
    }

    public static void ReloadConfig() => LoadConfig();

    private static void InitializeConfigPath()
    {
        try
        {
            var serverRoot = GetServerRootPath();
            var configFolder = Path.Combine(serverRoot, CONFIG_FOLDER);
            _configPath = Path.Combine(configFolder, CONFIG_FILE);
        }
        catch
        {
            _configPath = Path.Combine(CONFIG_FOLDER, CONFIG_FILE);
        }
    }

    private static string GetServerRootPath()
    {
        try
        {
            var rootFolder = Server.rootFolder;
            return !string.IsNullOrEmpty(rootFolder) ? rootFolder : Environment.CurrentDirectory;
        }
        catch
        {
            return ".";
        }
    }

    private static void LoadConfig()
    {
        _isLoaded = false;
        _config = new ThoriumConfig();

        try
        {
            if (!File.Exists(_configPath)) return;
            ParseYaml(File.ReadAllText(_configPath));
            _isLoaded = true;
        }
        catch { }
    }

    private static bool SaveConfig()
    {
        try
        {
            var configDir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
                Directory.CreateDirectory(configDir);

            var content = $"ServerToken: \"{_config.ServerToken ?? ""}\"\nDebug: {_config.Debug.ToString().ToLowerInvariant()}";
            File.WriteAllText(_configPath, content);
            _isLoaded = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ParseYaml(string content)
    {
        foreach (var line in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed[0] == '#') continue;

            var colonIndex = trimmed.IndexOf(':');
            if (colonIndex <= 0) continue;

            var key = trimmed.Substring(0, colonIndex).Trim().ToLowerInvariant();
            var value = trimmed.Substring(colonIndex + 1).Trim();

            if (value.Length >= 2 && ((value[0] == '"' && value[value.Length - 1] == '"') ||
                                       (value[0] == '\'' && value[value.Length - 1] == '\'')))
                value = value.Substring(1, value.Length - 2);

            switch (key)
            {
                case "servertoken":
                case "server_token":
                    _config.ServerToken = value;
                    break;
                case "debug":
                    _config.Debug = value.ToLowerInvariant() == "true" || value == "1";
                    break;
            }
        }
    }

    public static void Log(string message)
    {
        if (_isLoaded && _config.Debug)
            Debug.Log(message);
    }

    public static void LogAlways(string message)
    {
        Debug.Log(message);
    }

    public static void LogError(string message)
    {
        Debug.LogError(message);
    }

    public static void LogWarning(string message)
    {
        Debug.LogWarning(message);
    }
}
