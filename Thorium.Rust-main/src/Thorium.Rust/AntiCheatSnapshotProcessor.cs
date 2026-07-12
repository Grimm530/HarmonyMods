using System;
using System.Collections;
using System.Collections.Generic;
using Thorium.Rust.Config;
using Thorium.Rust.Core;
using Thorium.Rust.Models;
using Thorium.Rust.Services;
using UnityEngine;

namespace Thorium.Rust;

public static class AntiCheatSnapshotProcessor
{
    private const int FLUSH_INTERVAL_SECONDS = 1;
    private const int MAX_SNAPSHOTS_PER_PLAYER = 500;
    private const int SNAPSHOT_POOL_CAPACITY = 16384; // Increased from 2048 to handle high player counts and tick rates
    private const int ANTI_CHEAT_SNAPSHOT_POOL_CAPACITY = 2048;

    private static readonly Dictionary<long, Queue<PlayerSnapshot>> _buffer = new(1200);
    private static readonly List<long> _keysToRemove = new(1200);
    private static readonly List<AntiCheatSnapshot> _batchSnapshots = new(1200);

    // Object pools to reduce GC - sized generously for high player counts and tick rates
    // Pool capacity set high to avoid exhaustion and prevent fallback to allocations
    private static readonly Stack<PlayerSnapshot> _snapshotPool = new(SNAPSHOT_POOL_CAPACITY);
    private static readonly Stack<AntiCheatSnapshot> _antiCheatSnapshotPool = new(1200);
    private static readonly object _poolLock = new();

    private static bool _isRunning;
    private static Coroutine? _workerCoroutine;
    private static bool _isConfigured;
    private static float _lastConfigCheck;

    public static int BufferCount => _buffer.Count;
    public static bool IsWorkerRunning => _isRunning && _workerCoroutine != null;

    public static PlayerSnapshot GetPooledSnapshot()
    {
        lock (_poolLock)
        {
            if (_snapshotPool.Count > 0)
                return _snapshotPool.Pop();
        }
        return new PlayerSnapshot();
    }

    private static void ReturnSnapshotToPool(PlayerSnapshot snapshot)
    {
        // EventSnapshots should likely not be pooled as they are serialized differently and may contain unique data. This check prevents pooling them.
        if (snapshot is null or EventSnapshot) return;

        lock (_poolLock)
        {
            if (_snapshotPool.Count < SNAPSHOT_POOL_CAPACITY)
                _snapshotPool.Push(snapshot.ResetState());
        }
    }

    public static void Enqueue(long steamId, PlayerSnapshot snapshot)
    {
        if (steamId <= 0 || snapshot == null) return;

        var now = Time.realtimeSinceStartup;
        if (now - _lastConfigCheck > 5f)
        {
            _isConfigured = ThoriumConfigService.HasValidToken;
            _lastConfigCheck = now;
        }

        if (!_isConfigured)
        {
            ReturnSnapshotToPool(snapshot);
            return;
        }

        if (!_buffer.TryGetValue(steamId, out var queue))
        {
            queue = new Queue<PlayerSnapshot>(64);
            _buffer[steamId] = queue;
        }

        if (queue.Count >= MAX_SNAPSHOTS_PER_PLAYER)
        {
            var removed = queue.Dequeue();
            ReturnSnapshotToPool(removed);
        }

        queue.Enqueue(snapshot);
    }

    public static void StartWorker()
    {
        if (_isRunning) return;
        _isRunning = true;
        _isConfigured = ThoriumConfigService.HasValidToken;
        _lastConfigCheck = Time.realtimeSinceStartup;
        _workerCoroutine = ThoriumUnityScheduler.RunCoroutine(WorkerRoutine());
    }

    public static void StopWorker()
    {
        if (!_isRunning) return;
        _isRunning = false;

        ThoriumUnityScheduler.TryStopCoroutine(ref _workerCoroutine);

        FlushAll();
    }

    public static void CleanupPlayer(long steamId)
    {
        if (steamId <= 0)
            return;

        if (!_buffer.TryGetValue(steamId, out var snapshots))
            return;

        foreach (var snapshot in snapshots)
            ReturnSnapshotToPool(snapshot);

        snapshots.Clear();
        _buffer.Remove(steamId);
    }

    public static void Reset()
    {
        StopWorker();

        foreach (var kvp in _buffer)
        {
            var snapshots = kvp.Value;
            foreach (var snapshot in snapshots)
                ReturnSnapshotToPool(snapshot);
            snapshots.Clear();
        }

        _buffer.Clear();
    }

    private static IEnumerator WorkerRoutine()
    {
        var nextFlush = Time.realtimeSinceStartup + FLUSH_INTERVAL_SECONDS;

        while (_isRunning)
        {
            if (Time.realtimeSinceStartup >= nextFlush)
            {
                FlushAll();
                nextFlush = Time.realtimeSinceStartup + FLUSH_INTERVAL_SECONDS;
            }
            yield return null;
        }
    }

    private static void FlushAll()
    {
        _batchSnapshots.Clear();
        _keysToRemove.Clear();

        foreach (var kvp in _buffer)
        {
            var steamId = kvp.Key;
            var snapshots = kvp.Value;

            if (snapshots.Count == 0)
            {
                _keysToRemove.Add(steamId);
                continue;
            }

            AntiCheatSnapshot antiCheatSnapshot;
            lock (_poolLock)
            {
                antiCheatSnapshot = _antiCheatSnapshotPool.Count > 0
                    ? _antiCheatSnapshotPool.Pop()
                    : new AntiCheatSnapshot { Snapshots = new List<PlayerSnapshot>(64) };
            }

            antiCheatSnapshot.SteamId = steamId;
            antiCheatSnapshot.Snapshots.Clear();
            antiCheatSnapshot.Snapshots.AddRange(snapshots);

            _batchSnapshots.Add(antiCheatSnapshot);

            snapshots.Clear();
        }

        for (var i = 0; i < _keysToRemove.Count; i++)
            _buffer.Remove(_keysToRemove[i]);

        try
        {
            if (!ThoriumConfigService.HasValidToken)
                return;

            var caches = DataHandlerPayload.TryDrainAndReset();

            if (_batchSnapshots.Count == 0 && caches == null)
                return;

            var batch = new ThoriumBatch { StartTick = 0, EndTick = 0 };
            batch.Snapshots.AddRange(_batchSnapshots);

            var payload = ThoriumBatchProtobufSerializer.Serialize(batch, caches);

            _ = ThoriumClientService.SendBinaryOrQueueAsync(payload);

            // Return to pool after serialization
            lock (_poolLock)
            {
                foreach (var acs in _batchSnapshots)
                {
                    foreach (var snapshot in acs.Snapshots)
                        ReturnSnapshotToPool(snapshot);
                    for (var i = 0; i < acs.Snapshots.Count; i++)
                        ReturnSnapshotToPool(acs.Snapshots[i]);

                    acs.Snapshots.Clear();
                    if (_antiCheatSnapshotPool.Count < ANTI_CHEAT_SNAPSHOT_POOL_CAPACITY) // Increased from 512
                        _antiCheatSnapshotPool.Push(acs);
                }
            }
        } catch (Exception ex)
        {
            Debug.LogError($"Error flushing anti-cheat snapshots: {ex}");
        }
    }
}