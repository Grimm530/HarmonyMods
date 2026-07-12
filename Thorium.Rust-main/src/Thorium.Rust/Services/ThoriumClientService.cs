using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConVar;
using Newtonsoft.Json;
using Thorium.Rust.Config;
using Thorium.Rust.Core;
using Thorium.Rust.Models;
using UnityEngine;

namespace Thorium.Rust.Services;

/// <summary>
/// Service for managing WebSocket connection to Thorium backend.
/// Handles connection, messaging, and reconnection logic.
/// </summary>
public static class ThoriumClientService
{
    #region Constants

    private const int BUFFER_SIZE = 4096;
    private const int RECONNECT_INTERVAL_SECONDS = 60;
    private const int MAX_PENDING_TEXT_MESSAGES = 120;
    private const int MAX_PENDING_BINARY_MESSAGES = 60; // ~1 minute of batches at 1/sec
    private const string SERVER_TOKEN_HEADER = "X-SERVER-TOKEN";
    private const string SESSION_TOKEN_HEADER = "X-SESSION-TOKEN";
    private const string AUTH_ENDPOINT = "/api/session/auth";
    private const string WS_ENDPOINT = "/api/anticheat/ws";

    #endregion

    #region Fields

    /// <summary>
    /// Gets the server token from the configuration service.
    /// Returns null if no token is configured.
    /// </summary>
    public static string? token => ThoriumConfigService.ServerToken;

    private static ClientWebSocket? _webSocket;
    private static bool _isConnected;
    private static bool _isConnecting;
    private static int _reconnectAttempts;
    private static string? _currentUri;
    private static string? _sessionToken;
    private static Coroutine? _receiveCoroutine;
    private static Coroutine? _reconnectCoroutine;
    private static Queue<string> _pendingMessages = new();
    private static Queue<byte[]> _pendingBinaryMessages = new();
    private static bool _isFlushingPending;
    private static bool _isSending;
    private static Models.ServerInfo? _serverInfo;
    private static string _mapHash = string.Empty;

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    #endregion

    #region Events

    /// <summary>
    /// Raised when a text message is received from the server
    /// </summary>
    public static event Action<string>? OnMessageReceived;

    /// <summary>
    /// Raised when a binary message is received from the server
    /// </summary>
    public static event Action<byte[]>? OnBinaryMessageReceived;

    /// <summary>
    /// Raised when successfully connected to the server
    /// </summary>
    public static event Action? OnConnected;

    /// <summary>
    /// Raised when disconnected from the server
    /// </summary>
    public static event Action? OnDisconnected;

    #endregion

    #region Public Methods

    public static void SetServerInfo(Models.ServerInfo serverInfo)
    {
        _serverInfo = serverInfo ?? throw new ArgumentNullException(nameof(serverInfo));
    }

    /// <summary>
    /// Connects to the specified WebSocket URI
    /// </summary>
    /// <param name="uri">The WebSocket URI to connect to</param>
    /// <returns>Task representing the async operation</returns>
    public static async Task ConnectAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentException("URI cannot be null or empty", nameof(uri));

        if (_isConnected || _isConnecting)
        {
            return;
        }

        _isConnecting = true;
        _currentUri = uri;

        var tcs = new TaskCompletionSource<bool>();
        ThoriumUnityScheduler.RunCoroutine(ConnectRoutine(uri, tcs));

        await tcs.Task;
    }

    /// <summary>
    /// Starts (or ensures) the reconnect loop is running. The loop retries every 60 seconds
    /// until a connection is re-established or the client is disposed/unloaded.
    /// </summary>
    public static void EnsureReconnectLoopRunning()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_currentUri))
                return;

            if (_reconnectCoroutine != null)
                return;

            _reconnectCoroutine = ThoriumUnityScheduler.RunCoroutine(ReconnectLoopRoutine());
        }
        catch
        {
        }
    }

    /// <summary>
    /// Sends a message to the server
    /// </summary>
    /// <param name="message">The message to send</param>
    /// <returns>Task representing the async operation</returns>
    public static async Task SendMessageAsync(string message)
    {
        if (string.IsNullOrEmpty(message))
            throw new ArgumentException("Message cannot be null or empty", nameof(message));

        if (!IsConnected)
            throw new InvalidOperationException("WebSocket is not connected");

        var tcs = new TaskCompletionSource<bool>();
        ThoriumUnityScheduler.RunCoroutine(SendTextRoutine(message, tcs));
        await tcs.Task;
    }

    /// <summary>
    /// Sends a binary message to the server.
    /// </summary>
    /// <param name="data">Binary payload</param>
    public static async Task SendBinaryAsync(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (!IsConnected)
            throw new InvalidOperationException("WebSocket is not connected");

        var tcs = new TaskCompletionSource<bool>();
        ThoriumUnityScheduler.RunCoroutine(SendBinaryRoutine(data, tcs));
        await tcs.Task;
    }

    /// <summary>
    /// Sends a binary payload immediately if connected, otherwise queues it for later.
    /// Mirrors SendJsonAsync behavior so callers don't have to care about transient disconnects.
    /// </summary>
    public static async Task SendBinaryOrQueueAsync(byte[] data)
    {
        if (data == null)
            throw new ArgumentNullException(nameof(data));

        if (!IsConnected)
        {
            EnqueueBinaryWithLimit(data);
            return;
        }

        try
        {
            await SendBinaryAsync(data);
        }
        catch (Exception ex)
        {
            Log.Debug($"Binary send failed, queuing: {ex.Message}");
            EnqueueBinaryWithLimit(data);
        }
    }

    /// <summary>
    /// Sends a JSON serialized message to the server
    /// </summary>
    /// <typeparam name="T">The type of the message</typeparam>
    /// <param name="message">The message to send</param>
    /// <returns>Task representing the async operation</returns>
    public static async Task SendJsonAsync<T>(T message) where T : class
    {
        if (message == null)
            throw new ArgumentNullException(nameof(message));

        // Serialize now and either send immediately or enqueue for later
        var json = JsonConvert.SerializeObject(message);

        // If not connected, enqueue and return immediately
        if (!IsConnected)
        {
            EnqueueTextWithLimit(json);
            return;
        }

        try
        {
            await SendMessageAsync(json);
        }
        catch (Exception ex)
        {
            Log.Debug($"Send failed, queuing for retry: {ex.Message}");
            EnqueueTextWithLimit(json);
            // Do not re-throw - keep caller safe if they didn't await the task
        }
    }

    /// <summary>
    /// Gets the number of pending queued messages
    /// </summary>
    public static int PendingQueueCount => _pendingMessages.Count;

    public static int PendingBinaryQueueCount => _pendingBinaryMessages.Count;

    /// <summary>
    /// Attempts to deserialize a string as a JSON message
    /// </summary>
    /// <typeparam name="T">The type to deserialize to</typeparam>
    /// <param name="json">The JSON string to deserialize</param>
    /// <returns>The deserialized message, or null if deserialization fails</returns>
    public static T? DeserializeJson<T>(string json) where T : class
    {
        try
        {
            return JsonConvert.DeserializeObject<T>(json);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to deserialize JSON: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Flush queued messages (sends sequentially). Re-enqueues on transient failures.
    /// </summary>
    private static void StartFlushPendingMessages()
    {
        if (_isFlushingPending)
            return;

        _isFlushingPending = true;
        ThoriumUnityScheduler.RunCoroutine(FlushPendingMessagesRoutine());
    }

    /// <summary>
    /// Disconnects from the server
    /// </summary>
    /// <returns>Task representing the async operation</returns>
    public static async Task DisconnectAsync()
    {
        if (!_isConnected && !_isConnecting)
            return;

        var tcs = new TaskCompletionSource<bool>();
        ThoriumUnityScheduler.RunCoroutine(DisconnectRoutine(tcs));
        await tcs.Task;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets whether the client is currently connected
    /// </summary>
    public static bool IsConnected => _isConnected && _webSocket?.State == WebSocketState.Open;

    /// <summary>
    /// Gets whether the client has a valid server token configured
    /// </summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(token);

    #endregion

    #region Reset

    public static void Reset()
    {
        try
        {
            _ = DisconnectAsync();
        }
        catch
        {
        }
        finally
        {
            DisposeWebSocket();
        }

        OnMessageReceived = null;
        OnBinaryMessageReceived = null;
        OnConnected = null;
        OnDisconnected = null;
        _pendingMessages.Clear();
        _pendingBinaryMessages.Clear();
        _isConnected = false;
        _isConnecting = false;
        _reconnectAttempts = 0;
        _currentUri = null;
        _sessionToken = null;
        _receiveCoroutine = null;
        _reconnectCoroutine = null;
        _isFlushingPending = false;
        _isSending = false;
        _serverInfo = null;
        _mapHash = string.Empty;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Initializes the WebSocket and cancellation token
    /// </summary>
    private static void InitializeWebSocket()
    {
        _webSocket = new ClientWebSocket();

        try
        {
            if (!string.IsNullOrWhiteSpace(_sessionToken))
            {
                _webSocket.Options.SetRequestHeader(SESSION_TOKEN_HEADER, _sessionToken);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Failed to set session token header: {ex.Message}");
        }
    }

    /// <summary>
    /// Authenticates with the backend API to get a session token
    /// </summary>
    private static IEnumerator AuthenticateRoutine(string hostname, TaskCompletionSource<bool> tcs)
    {
        Task<string>? authTask = null;
        Exception? authEx = null;

        try
        {
            var authUrl = $"https://{hostname}{AUTH_ENDPOINT}";

            Log.Debug($"Authenticating with: {authUrl}");

            authTask = AuthenticateAsync(authUrl);
        }
        catch (Exception ex)
        {
            authEx = ex;
        }

        if (authEx != null || authTask == null)
        {
            Log.Error($"Failed to start authentication: {authEx?.Message ?? "Unknown error"}");
            tcs.TrySetException(
                new InvalidOperationException($"Failed to authenticate: {authEx?.Message ?? "Unknown error"}", authEx));
            yield break;
        }

        while (!authTask.IsCompleted)
            yield return null;

        if (authTask.IsFaulted)
        {
            var ex = authTask.Exception?.GetBaseException() ?? new InvalidOperationException("Authentication failed");
            Log.Error($"Authentication failed: {ex.Message}");
            tcs.TrySetException(new InvalidOperationException($"Failed to authenticate: {ex.Message}", ex));
            yield break;
        }

        _sessionToken = authTask.Result;
        Log.Debug("Authentication successful");
        tcs.TrySetResult(true);
    }

    /// <summary>
    /// Performs the actual HTTP authentication request
    /// </summary>
    private static async Task<string> AuthenticateAsync(string authUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, authUrl);
        request.Headers.Add(SERVER_TOKEN_HEADER, token);

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Authentication failed with status {response.StatusCode}: {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        Log.Debug($"Auth response: {content}");

        var authResponse = JsonConvert.DeserializeObject<AuthResponse>(content);

        if (authResponse == null)
            throw new InvalidOperationException($"Failed to deserialize authentication response. Body: {content}");

        if (string.IsNullOrWhiteSpace(authResponse.SessionToken))
            throw new InvalidOperationException(
                $"Authentication response did not contain a valid session token. Body: {content}");

        Log.Debug(
            $"Received session token: {authResponse.SessionToken.Substring(0, Math.Min(10, authResponse.SessionToken.Length))}...");
        return authResponse.SessionToken;
    }

    /// <summary>
    /// Response model for authentication endpoint
    /// </summary>
    private class AuthResponse
    {
        [JsonProperty("sessionToken")] public string SessionToken { get; set; } = string.Empty;
    }

    private static async Task SendLevelToBackendAsync()
    {
        Log.Debug("SendLevelToBackendAsync started");
        var levelUrl = Server.levelurl;
        Log.Debug($"Level URL: '{levelUrl}'");

        if (string.IsNullOrWhiteSpace(levelUrl))
        {
            Log.Debug("Level URL empty, uploading map data from file...");
            await UploadMapDataAsync();
        }
        else
        {
            Log.Debug($"Uploading level URL: {levelUrl}");
            await UploadLevelUrlAsync(levelUrl);
        }

        Log.Debug($"SendLevelToBackendAsync completed. MapHash: '{_mapHash}'");
    }

    private static async Task UploadLevelUrlAsync(string levelUrl)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{_currentUri}/api/maps/levelurl");
            request.Headers.Add(SERVER_TOKEN_HEADER, token);
            var payload = new { levelUrl };
            var jsonPayload = JsonConvert.SerializeObject(payload);
            request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            Log.Debug($"Uploading level URL: {levelUrl}");
            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error($"UploadLevelUrlAsync failed: {response.StatusCode}: {errorContent}");
                return;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            Log.Debug($"Level URL upload response: {responseContent}");
            TrySetMapHashFromResponse(responseContent);
        }
        catch (Exception ex)
        {
            Log.Error($"UploadLevelUrlAsync exception: {ex.Message}");
        }
    }

    private static async Task UploadMapDataAsync()
    {
        try
        {
            var mapPath = Server.rootFolder + "/" + World.MapFileName;
            var size = Server.worldsize;
            var seed = Server.seed;

            if (!File.Exists(mapPath))
            {
                Log.Debug($"Map file not found: {mapPath}");
                return;
            }

            Log.Debug($"Uploading map data: {mapPath} (size: {size}, seed: {seed})");
            var mapData = await File.ReadAllBytesAsync(mapPath);

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://{_currentUri}/api/maps/upload?mapSize={size}&mapSeed={seed}");
            request.Headers.Add(SERVER_TOKEN_HEADER, token);
            request.Content = new ByteArrayContent(mapData);

            using var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error($"UploadMapDataAsync failed: {response.StatusCode}: {errorContent}");
                return;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            Log.Debug($"Map upload response: {responseContent}");
            TrySetMapHashFromResponse(responseContent);
        }
        catch (Exception ex)
        {
            Log.Error($"UploadMapDataAsync exception: {ex.Message}");
        }
    }

    private static void TrySetMapHashFromResponse(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent)) return;
        var mapResponse = DeserializeJson<MapResponse>(responseContent);
        if (mapResponse != null && !string.IsNullOrWhiteSpace(mapResponse.Hash))
        {
            _mapHash = mapResponse.Hash;
            Log.Debug($"Map hash set to: {_mapHash}");
        }
    }

    private static IEnumerator ConnectRoutine(string hostname, TaskCompletionSource<bool> tcs)
    {
        var authTcs = new TaskCompletionSource<bool>();
        yield return AuthenticateRoutine(hostname, authTcs);

        if (authTcs.Task.IsFaulted)
        {
            _isConnecting = false;
            var authEx = authTcs.Task.Exception?.GetBaseException() ??
                         new InvalidOperationException("Authentication failed");
            Log.Error($"Failed to authenticate: {authEx.Message}");
            EnsureReconnectLoopRunning();
            tcs.TrySetException(authEx);
            yield break;
        }

        Log.Debug("Starting SendLevelToBackendAsync...");
        Task sendLevelTask = null;

        try
        {
            sendLevelTask = SendLevelToBackendAsync();
        }
        catch (Exception ex)
        {
            Log.Debug($"SendLevelToBackendAsync failed to start: {ex.Message}");
        }

        if (sendLevelTask != null)
        {
            while (!sendLevelTask.IsCompleted)
                yield return null;

            if (sendLevelTask.IsFaulted)
            {
                var ex = sendLevelTask.Exception?.GetBaseException();
                Log.Debug($"SendLevelToBackendAsync faulted: {ex?.Message}");
            }
        }

        Task connectTask = null;
        Exception connectEx = null;

        try
        {
            DisposeWebSocket();
            InitializeWebSocket();

            if (_webSocket == null)
                throw new InvalidOperationException("ClientWebSocket initialization failed");

            var wsUri = $"wss://{hostname}{WS_ENDPOINT}";
            Log.Debug($"Connecting to: {wsUri}");
            connectTask = _webSocket.ConnectAsync(new Uri(wsUri), CancellationToken.None);
        }
        catch (Exception ex)
        {
            connectEx = ex;
        }

        if (connectEx != null || connectTask == null)
        {
            _isConnecting = false;
            Log.Error($"Failed to connect: {connectEx?.Message ?? "Unknown error"}");
            tcs.TrySetException(
                new InvalidOperationException($"Failed to connect: {connectEx?.Message ?? "Unknown error"}",
                    connectEx));
            yield break;
        }

        while (!connectTask.IsCompleted)
            yield return null;

        if (connectTask.IsFaulted)
        {
            var ex = connectTask.Exception?.GetBaseException() ?? new InvalidOperationException("Connect failed");
            _isConnecting = false;
            Log.Error($"Failed to connect: {ex.Message}");
            EnsureReconnectLoopRunning();
            tcs.TrySetException(new InvalidOperationException($"Failed to connect: {ex.Message}", ex));
            yield break;
        }

        _isConnected = true;
        _isConnecting = false;
        _reconnectAttempts = 0;

        Log.Info("Connected to Thorium backend");
        OnConnected?.Invoke();

        // Stop any reconnect loop once connected.
        ThoriumUnityScheduler.TryStopCoroutine(ref _reconnectCoroutine);

        // Send server info as initial text message if available
        if (_serverInfo != null)
        {
            Exception sendEx = null;
            TaskCompletionSource<bool> sendTcs = null;

            // Resolve external IP outside try/catch (yield return not allowed in try/catch)
            var resolvedIp = Server.ip;
            {
                var ipTask = _httpClient.GetStringAsync("https://api.ipify.org");
                while (!ipTask.IsCompleted)
                    yield return null;
                if (!ipTask.IsFaulted && !string.IsNullOrWhiteSpace(ipTask.Result))
                    resolvedIp = ipTask.Result.Trim();
            }

            try
            {
                _serverInfo.HostName = Server.hostname;
                _serverInfo.Port = Server.port;
                _serverInfo.IpAddress = resolvedIp;
                _serverInfo.MapHash = _mapHash;
                var serverInfoJson = JsonConvert.SerializeObject(_serverInfo);
                Log.Debug($"Sending server info: {serverInfoJson}");
                sendTcs = new TaskCompletionSource<bool>();
                ThoriumUnityScheduler.RunCoroutine(SendTextRoutine(serverInfoJson, sendTcs));
            }
            catch (Exception ex)
            {
                sendEx = ex;
            }

            if (sendTcs != null && sendEx == null)
            {
                while (!sendTcs.Task.IsCompleted)
                    yield return null;

                if (sendTcs.Task.IsFaulted)
                {
                    Log.Debug($"Failed to send server info: {sendTcs.Task.Exception?.GetBaseException().Message}");
                }
            }
            else if (sendEx != null)
            {
                Log.Debug($"Error sending server info: {sendEx.Message}");
            }
        }

        StartFlushPendingMessages();
        StartReceiveLoop();

        ThoriumUnityScheduler.RunCoroutine(SendInitialEntitiesRoutine());

        tcs.TrySetResult(true);
    }

    private static IEnumerator SendInitialEntitiesRoutine()
    {
        const int maxSnapshotAttempts = 5;

        const int batchSize = 1000;
        const int sendThreshold = 50000;
        var entityCount = 0;
        var total = 0;
        var batchesSent = 0;

        Log.Debug("Starting initial entity sync...");

        List<BaseEntity>? snapshot = null;
        for (var attempt = 1; attempt <= maxSnapshotAttempts; attempt++)
        {
            var retryAfterFailure = false;
            var realm = BaseNetworkable.serverEntities;
            if (realm == null)
            {
                Log.Debug($"Initial entity sync: serverEntities not ready (attempt {attempt}/{maxSnapshotAttempts})");
                yield return null;
                continue;
            }

            try
            {
                snapshot = new List<BaseEntity>();
                foreach (var networkable in realm)
                {
                    if (networkable is BaseEntity entity)
                        snapshot.Add(entity);
                }

                if (snapshot.Count > 0)
                    break;

                snapshot = null;
                Log.Debug($"Initial entity sync: no entities found (attempt {attempt}/{maxSnapshotAttempts})");
                retryAfterFailure = true;
            }
            catch (Exception ex)
            {
                snapshot = null;
                Log.Debug($"Initial entity sync snapshot failed (attempt {attempt}/{maxSnapshotAttempts}): {ex.Message}");
                retryAfterFailure = true;
            }

            if (retryAfterFailure)
            {
                yield return null;
            }
        }

        if (snapshot == null || snapshot.Count == 0)
        {
            Log.Warning("Initial entity sync aborted: could not capture stable server entity snapshot");
            yield break;
        }

        Log.Debug($"Initial entity snapshot captured: {snapshot.Count} entities");

        var localCache = new MemoryStream(1 << 24); // 16 MB initial
        long localEntityPackets = 0;
        long sentSoFar = 0;
        var totalExpected = snapshot.Count;

        try
        {
            foreach (var networkable in snapshot)
            {
                var entity = networkable;

                try
                {
                    var startPos = localCache.Position;
                    var ownerId = entity.OwnerID;
                    ProtoBufManager.WriteBool(localCache, true);
                    ProtoBufManager.WriteInt64(localCache, (long)entity.net.ID.Value);
                    ProtoBufManager.WriteString(localCache, ownerId > 0 ? ownerId.ToString() : string.Empty);
                    ProtoBufManager.WriteUint(localCache, entity.prefabID);
                    ProtoBufManager.WriteString(localCache, entity.ShortPrefabName ?? string.Empty);
                    ProtoBufManager.WriteVector(localCache, entity.ServerPosition);
                    ProtoBufManager.WriteVector(localCache, entity.ServerRotation.eulerAngles);
                    ProtoBufManager.WriteVector(localCache, entity.CenterPoint());
                    ProtoBufManager.WriteVector(localCache, entity.bounds.extents);

                    if (total == 0)
                    {
                        var endPos = localCache.Position;
                        var entitySize = endPos - startPos;
                        localCache.Position = startPos;
                        var firstBytes = new byte[Math.Min(entitySize, 64)];
                        localCache.Read(firstBytes, 0, firstBytes.Length);
                        localCache.Position = endPos;
                    }

                    localEntityPackets++;
                    total++;
                }
                catch
                {
                }

                if (++entityCount >= batchSize)
                {
                    entityCount = 0;

                    if (localEntityPackets >= sendThreshold)
                    {
                        var batchPackets = localEntityPackets;
                        var flush = FlushLocalEntityBatchRoutine(localCache, localEntityPackets);
                        while (flush.MoveNext()) yield return flush.Current;
                        batchesSent++;
                        sentSoFar += batchPackets;
                        var remaining = Math.Max(0, totalExpected - sentSoFar);
                        Log.Debug($"Entity Sync: {sentSoFar} sent / {remaining} remaining");
                        localCache = new MemoryStream(1 << 24);
                        localEntityPackets = 0;
                    }
                    else
                    {
                        yield return null;
                    }
                }
            }

            if (localEntityPackets > 0)
            {
                var batchPackets = localEntityPackets;
                var flush = FlushLocalEntityBatchRoutine(localCache, localEntityPackets);
                while (flush.MoveNext()) yield return flush.Current;
                batchesSent++;
                sentSoFar += batchPackets;
                var remaining = Math.Max(0, totalExpected - sentSoFar);
                Log.Debug($"Entity Sync: {sentSoFar} sent / {remaining} remaining");
            }

            Log.Debug($"Initial entity sync complete: {total} entities in {batchesSent} batches");
        }
        finally
        {
            localCache.Dispose();
        }
    }

    private static IEnumerator FlushLocalEntityBatchRoutine(MemoryStream cache, long entityPackets)
    {
        int length;
        byte[] bytes;
        byte[] serialized;
        try
        {
            length = (int)cache.Length;
            if (length <= 0) yield break;

            bytes = new byte[length];
            if (cache.TryGetBuffer(out var seg))
                Array.Copy(seg.Array!, seg.Offset, bytes, 0, length);
            else
            {
                cache.Position = 0;
                _ = cache.Read(bytes, 0, length);
            }
            cache.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to prepare initial entity batch: {ex.Message}");
            yield break;
        }

        // Split expensive copy/serialize work across frames to avoid long frame stalls.
        yield return null;

        try
        {
            var payload = new DataHandlerPayload
            {
                EntityCacheBytes = bytes,
                TotalEntityPackets = entityPackets
            };

            var batch = new ThoriumBatch { StartTick = 0, EndTick = 0 };
            serialized = ThoriumBatchProtobufSerializer.Serialize(batch, payload);

        }
        catch (Exception ex)
        {
            Log.Error($"Failed to serialize initial entity batch: {ex.Message}");
            yield break;
        }

        if (!IsConnected)
        {
            EnqueueBinaryWithLimit(serialized);
            yield break;
        }

        // Yield once before network send to keep this coroutine cooperative under load.
        yield return null;

        var tcs = new TaskCompletionSource<bool>();
        ThoriumUnityScheduler.RunCoroutine(SendBinaryRoutine(serialized, tcs));

        while (!tcs.Task.IsCompleted)
            yield return null;

        if (tcs.Task.IsFaulted)
        {
            Log.Error($"Failed to send entity batch: {tcs.Task.Exception?.GetBaseException().Message}");
            EnqueueBinaryWithLimit(serialized);
        }
        else
        {
        }
    }

    private static string GetIpAddress()
    {
        // Never block the Unity main thread with synchronous network I/O.
        return Server.ip;
    }

    private static bool IsLocalIp(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a)) return false;
        var b = a.GetAddressBytes();
        return IPAddress.IsLoopback(a) ||
               b[0] == 10 ||
               (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
               (b[0] == 192 && b[1] == 168);
    }

    private static void StartReceiveLoop()
    {
        ThoriumUnityScheduler.TryStopCoroutine(ref _receiveCoroutine);
        _receiveCoroutine = ThoriumUnityScheduler.RunCoroutine(ReceiveLoopRoutine());
    }

    private static IEnumerator SendTextRoutine(string message, TaskCompletionSource<bool> tcs)
    {
        return SendRoutine(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, tcs);
    }

    private static IEnumerator SendBinaryRoutine(byte[] data, TaskCompletionSource<bool> tcs)
    {
        return SendRoutine(data, WebSocketMessageType.Binary, tcs);
    }

    private static IEnumerator SendRoutine(byte[] data, WebSocketMessageType messageType,
        TaskCompletionSource<bool> tcs)
    {
        // Wait for any in-flight send to complete — ClientWebSocket does not support concurrent sends
        while (_isSending)
            yield return null;

        _isSending = true;

        Task sendTask = null;
        Exception sendEx = null;

        try
        {
            if (_webSocket == null)
                throw new InvalidOperationException("WebSocket is not initialized");

            sendTask = _webSocket.SendAsync(new ArraySegment<byte>(data), messageType, true,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            sendEx = ex;
        }

        if (sendEx != null || sendTask == null)
        {
            _isSending = false;
            Log.Error($"Failed to send {messageType}: {sendEx?.Message ?? "Unknown error"}");
            HandleConnectionError();
            tcs.TrySetException(sendEx ?? new InvalidOperationException("Send failed"));
            yield break;
        }

        while (!sendTask.IsCompleted)
            yield return null;

        _isSending = false;

        if (sendTask.IsFaulted)
        {
            var ex = sendTask.Exception?.GetBaseException() ?? new InvalidOperationException("Send failed");
            Log.Error($"Failed to send {messageType}: {ex.Message}");
            HandleConnectionError();
            tcs.TrySetException(ex);
            yield break;
        }

        tcs.TrySetResult(true);
    }

    private static IEnumerator FlushPendingMessagesRoutine()
    {
        try
        {
            while (IsConnected && (_pendingMessages.Count > 0 || _pendingBinaryMessages.Count > 0))
            {
                if (_pendingMessages.Count > 0)
                {
                    var json = _pendingMessages.Dequeue();

                    var tcs = new TaskCompletionSource<bool>();
                    ThoriumUnityScheduler.RunCoroutine(SendTextRoutine(json, tcs));
                    while (!tcs.Task.IsCompleted)
                        yield return null;

                    if (tcs.Task.IsFaulted)
                    {
                        Log.Debug("Failed to flush queued text, re-queueing");
                        EnqueueTextWithLimit(json);
                        yield break;
                    }
                }

                if (_pendingBinaryMessages.Count > 0)
                {
                    var data = _pendingBinaryMessages.Dequeue();

                    var tcs = new TaskCompletionSource<bool>();
                    ThoriumUnityScheduler.RunCoroutine(SendBinaryRoutine(data, tcs));
                    while (!tcs.Task.IsCompleted)
                        yield return null;

                    if (tcs.Task.IsFaulted)
                    {
                        Log.Debug("Failed to flush queued binary, re-queueing");
                        _pendingBinaryMessages.Enqueue(data);
                        yield break;
                    }
                }

                yield return null;
            }
        }
        finally
        {
            _isFlushingPending = false;
        }
    }

    private static IEnumerator ReceiveLoopRoutine()
    {
        var buffer = new byte[BUFFER_SIZE];

        while (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            Task<WebSocketReceiveResult> receiveTask = null;
            Exception startReceiveEx = null;

            try
            {
                receiveTask = _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                startReceiveEx = ex;
            }

            if (startReceiveEx != null || receiveTask == null)
            {
                Log.Error($"Error starting receive: {startReceiveEx?.Message ?? "Unknown error"}");
                HandleConnectionError();
                yield break;
            }

            while (!receiveTask.IsCompleted)
                yield return null;

            if (receiveTask.IsFaulted)
            {
                var msg = receiveTask.Exception?.GetBaseException().Message ?? "Unknown error";
                Log.Error($"Error in receive loop: {msg}");
                HandleConnectionError();
                yield break;
            }

            var result = receiveTask.Result;

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                OnMessageReceived?.Invoke(message);
            }
            else if (result.MessageType == WebSocketMessageType.Binary)
            {
                var data = new byte[result.Count];
                Array.Copy(buffer, data, result.Count);
                OnBinaryMessageReceived?.Invoke(data);
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
                Log.Debug("Server closed connection");
                HandleConnectionError();
                yield break;
            }

            yield return null;
        }
    }

    private static void HandleConnectionError()
    {
        _isConnected = false;
        _isConnecting = false;
        OnDisconnected?.Invoke();

        _reconnectAttempts = 0;
        EnsureReconnectLoopRunning();
    }

    private static IEnumerator ReconnectLoopRoutine()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(RECONNECT_INTERVAL_SECONDS);

            if (string.IsNullOrWhiteSpace(_currentUri))
                continue;

            if (IsConnected)
                yield break;

            if (_isConnecting)
                continue;

            _reconnectAttempts++;
            Log.Debug($"Reconnect attempt #{_reconnectAttempts}...");

            var tcs = new TaskCompletionSource<bool>();
            _isConnecting = true;
            yield return ConnectRoutine(_currentUri, tcs);

            if (tcs.Task.IsFaulted)
            {
                var ex = tcs.Task.Exception?.GetBaseException();
                Log.Debug($"Reconnect failed: {ex?.Message ?? "Unknown error"}");
            }

            if (IsConnected)
                yield break;
        }
    }

    private static IEnumerator DisconnectRoutine(TaskCompletionSource<bool> tcs)
    {
        Log.Debug("Disconnecting from server");

        _isConnected = false;
        _isConnecting = false;

        ThoriumUnityScheduler.TryStopCoroutine(ref _receiveCoroutine);
        ThoriumUnityScheduler.TryStopCoroutine(ref _reconnectCoroutine);

        Task closeTask = null;
        Exception closeEx = null;

        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            try
            {
                closeTask = _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting",
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                closeEx = ex;
            }
        }

        if (closeEx != null)
            Log.Debug($"Error during disconnect: {closeEx.Message}");

        if (closeTask != null)
        {
            while (!closeTask.IsCompleted)
                yield return null;
        }

        OnDisconnected?.Invoke();
        DisposeWebSocket();
        tcs.TrySetResult(true);
    }

    private static void EnqueueTextWithLimit(string json)
    {
        while (_pendingMessages.Count >= MAX_PENDING_TEXT_MESSAGES)
            _pendingMessages.Dequeue();
        _pendingMessages.Enqueue(json);
    }

    private static void EnqueueBinaryWithLimit(byte[] data)
    {
        while (_pendingBinaryMessages.Count >= MAX_PENDING_BINARY_MESSAGES)
            _pendingBinaryMessages.Dequeue();
        _pendingBinaryMessages.Enqueue(data);
    }

    private static void DisposeWebSocket()
    {
        try
        {
            _webSocket?.Dispose();
            _webSocket = null;
        }
        catch
        {
            _webSocket = null;
        }
    }

    #endregion
}