using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConVar;
using Thorium.Rust.Config;
using Thorium.Rust.Core;
using Thorium.Rust.Services;
using Time = UnityEngine.Time;

namespace Thorium.Rust;

/// <summary>
/// Main loader class for the Thorium anti-cheat system
/// Handles mod initialization, patching, and service management
/// </summary>
public class ThoriumLoader : IHarmonyModHooks
{
    #region Constants

    public const string BACKEND_URI = "gateway.thorium.ac";
    private const int CONNECTION_TIMEOUT_MS = 5000;
    #endregion

    #region Static Fields
    public static bool __serverStarted;
    public static string MAP_HASH = "";

    // Legacy compatibility - keeping for existing code references
    public static Dictionary<uint, Action<BasePlayer, BaseEntity?>> rpcActions = new();
    #endregion

    #region IHarmonyModHooks Implementation
    /// <summary>
    /// Called when the Thorium mod is loaded by Harmony
    /// </summary>
    /// <param name="args">Mod loading arguments</param>
    public void OnLoaded(OnHarmonyModLoadedArgs args)
    {
        try
        {
            RegisterUnhandledExceptionHandler();
            InitializeOnMainThread();
            // If the mod is loaded after the server is already up (e.g. harmony.load),
            // we won't get an OpenConnection callback immediately. Kick a non-blocking
            // "start when ready" routine to ensure backend connection happens.
            ThoriumUnityScheduler.RunCoroutine(StartWhenServerReadyRoutine());
        }
        catch (Exception ex)
        {
            Log.Error($"Fatal error during mod loading: {ex.Message}");
        }
    }

    /// <summary>
    /// Called when the Thorium mod is unloaded
    /// </summary>
    /// <param name="args">Mod unloading arguments</param>
    public void OnUnloaded(OnHarmonyModUnloadedArgs args)
    {
        try
        {
            CleanupResources();
            Log.Info("ThoriumLoader unloaded successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Error during mod unloading: {ex.Message}");
        }
    }
    #endregion

    #region Server Initialization
    /// <summary>
    /// Called when the server has started and is ready for post-initialization
    /// </summary>
    public static void OnServerStarted()
    {
        if (__serverStarted)
            return;

        __serverStarted = true;
        Log.Info("Server started - beginning post-initialization");

        ThoriumUnityScheduler.RunCoroutine(ServerStartupRoutine());
    }
    #endregion

    #region Private Initialization Methods
    private static void RegisterUnhandledExceptionHandler()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error($"Unhandled exception: {e.ExceptionObject}");
        };
    }

    /// <summary>
    /// Performs initial setup without spawning worker threads.
    /// </summary>
    private void InitializeOnMainThread()
    {
        // Ensure the coroutine host exists so queued startup work can run.
        ThoriumUnityScheduler.EnsureInitialized();
        ThoriumConfigService.Initialize();
    }

    private static IEnumerator StartWhenServerReadyRoutine()
    {
        // Already started? Nothing to do.
        if (__serverStarted)
            yield break;

        // Wait until ServerMgr is available. This covers both:
        // - server already started (instant), and
        // - server still starting (poll until ready)
        while (ServerMgr.Instance == null)
            yield return null;

        // One more frame to let game systems settle.
        yield return null;

        OnServerStarted();
    }

    private static void SetupServerInfo()
    {
        try
        {
            var serverInfo = new Models.ServerInfo
            {
                HostName = Server.hostname ?? "Unknown Server",
                MapHash = MAP_HASH,
                IpAddress = Server.ip ?? "0.0.0.0",
                Port = Server.port
            };

            ThoriumClientService.SetServerInfo(serverInfo);
            Log.Debug($"Server info configured: {serverInfo.HostName} ({serverInfo.IpAddress}:{serverInfo.Port})");
        }
        catch (Exception ex)
        {
            Log.Warning($"Failed to setup server info: {ex.Message}");
        }
    }

    /// <summary>
    /// Connects to the Thorium backend service
    /// </summary>
    private static IEnumerator ConnectToBackendRoutine()
    {
        if (!ThoriumClientService.IsConfigured)
        {
            Log.Info("No server token configured. Run 'thorium.setup <token>' to enable.");
            yield break;
        }

        Task connectTask;
        try
        {
            connectTask = ThoriumClientService.ConnectAsync(BACKEND_URI);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to start backend connect: {ex.Message}");
            ThoriumClientService.EnsureReconnectLoopRunning();
            yield break;
        }

        var startTime = Time.realtimeSinceStartup;

        while (!connectTask.IsCompleted)
        {
            if ((Time.realtimeSinceStartup - startTime) * 1000f >= CONNECTION_TIMEOUT_MS)
            {
                Log.Warning("Backend connect timed out; retrying in background");
                ThoriumClientService.EnsureReconnectLoopRunning();
                yield break;
            }
            yield return null;
        }

        if (connectTask.IsFaulted)
        {
            var msg = connectTask.Exception?.GetBaseException().Message ?? "Unknown error";
            Log.Warning($"Failed to connect: {msg}. Retrying in background");
            ThoriumClientService.EnsureReconnectLoopRunning();
            yield break;
        }
    }

    private static IEnumerator ServerStartupRoutine()
    {
        SetupServerInfo();

        yield return ConnectToBackendRoutine();

        try
        {
            StartServices();
            RegisterConsoleCommands();
            Log.Info("Thorium initialized successfully");
        }
        catch (Exception ex)
        {
            Log.Error($"Critical error during startup: {ex.Message}");
            HandleCriticalError();
        }
    }

    private static void StartServices()
    {
        Log.Debug("Starting snapshot processor");
        AntiCheatSnapshotProcessor.StartWorker();
    }

    /// <summary>
    /// Registers console commands with the server
    /// </summary>
    private static void RegisterConsoleCommands()
    {
        Log.Info("Registering console commands");
        // Avoid crashing startup if ConsoleSystem isn't ready or commands can't be registered.
        try
        {
            ConsoleCommands.RegisterCommands();
            Log.Info("Console commands registered successfully");
        }
        catch (Exception ex)
        {
            Log.Warning($"Console command registration failed: {ex.Message}\n{ex}");
        }
    }
    #endregion

    #region Cleanup and Error Handling
    /// <summary>
    /// Handles critical errors that prevent the mod from functioning
    /// </summary>
    private static void HandleCriticalError()
    {
        try
        {
            Log.Error("Attempting to unload mod due to critical error");
            HarmonyLoader.TryUnloadMod("Thorium");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to unload mod after critical error: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans up all resources when the mod is unloaded
    /// </summary>
    private static void CleanupResources()
    {
        // Reset server state
        __serverStarted = false;

        // Stop services before disposing
        try
        {
            Log.Info("Stopping snapshot processor...");
            AntiCheatSnapshotProcessor.StopWorker();
        }
        catch (Exception ex)
        {
            Log.Warning($"Error stopping snapshot processor: {ex.Message}\n{ex}");
        }

        try
        {
            Log.Info("Disconnecting backend client...");
            _ = ThoriumClientService.DisconnectAsync();
        }
        catch (Exception ex)
        {
            Log.Warning($"Error disconnecting backend client: {ex.Message}\n{ex}");
        }

        // Reset all services so a subsequent harmony.load starts cleanly.
        try { ThoriumClientService.Reset(); } catch { }
        try { AntiCheatSnapshotProcessor.Reset(); } catch { }
        ConsoleCommands.Reset();
        try { DataHandler.Reset(); } catch { }
        ThoriumConfigService.Reset();

        try
        {
            Log.Info("Destroying Unity scheduler...");
            ThoriumUnityScheduler.DestroyInstance();
        }
        catch (Exception ex)
        {
            Log.Warning($"Error destroying Unity scheduler: {ex.Message}\n{ex}");
        }

        Log.Info("Resource cleanup completed");
    }
    #endregion
}