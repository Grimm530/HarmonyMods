using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace HarmonyMetrics;

internal class ReportUploader : MonoBehaviour
{
    private const int SendBufferCapacity = 100000;
    private const float OfflineBackoffSeconds = 30f;
    private const int RequestTimeoutSeconds = 3;

    private readonly Action _notifySubsequentNetworkFailuresAction;
    private readonly Action _notifySubsequentHttpFailuresAction;
    private readonly Queue<string> _sendBuffer = new Queue<string>(SendBufferCapacity);
    private readonly StringBuilder _payloadBuilder = new StringBuilder();

    private bool _isRunning;
    private ushort _attempt;
    private byte[] _data;
    private Uri _uri;
    private MetricsLogger _metricsLogger;
    private char[] _charBuffer = new char[8192 * 4];
    private bool _throttleNetworkErrorMessages;
    private uint _accumulatedNetworkErrors;
    private bool _throttleHttpErrorMessages;
    private uint _accumulatedHttpErrors;
    private float _offlineUntil;
    private bool _offlineLogged;

    private ushort BatchSize
    {
        get
        {
            var configVal = _metricsLogger != null && _metricsLogger.Configuration != null
                ? _metricsLogger.Configuration.BatchSize
                : (ushort)1000;
            return configVal < 1000 ? (ushort)1000 : configVal;
        }
    }

    public bool IsRunning => _isRunning;
    public int BufferSize => _sendBuffer.Count;
    public bool IsOffline => Time.realtimeSinceStartup < _offlineUntil;

    public ReportUploader()
    {
        _notifySubsequentNetworkFailuresAction = NotifySubsequentNetworkFailures;
        _notifySubsequentHttpFailuresAction = NotifySubsequentHttpFailures;
    }

    private void Awake()
    {
        _metricsLogger = GetComponent<MetricsLogger>();
        if (_metricsLogger == null)
        {
            Debug.LogError("[HarmonyMetrics] ReportUploader failed to find the MetricsLogger component");
            Destroy(this);
        }
    }

    public void AddToSendBuffer(string payload)
    {
        if (IsOffline)
        {
            // Drop while Influx is unreachable so the queue cannot grow without bound.
            return;
        }

        if (_sendBuffer.Count == SendBufferCapacity)
        {
            _sendBuffer.Dequeue();
        }

        _sendBuffer.Enqueue(payload);

        if (!_isRunning)
        {
            StartCoroutine(SendBufferLoop());
        }
    }

    private IEnumerator SendBufferLoop()
    {
        _isRunning = true;
        yield return null;

        while (_sendBuffer.Count > 0 && _isRunning)
        {
            if (IsOffline)
            {
                _sendBuffer.Clear();
                break;
            }

            var amountToTake = Mathf.Min(_sendBuffer.Count, BatchSize);
            for (var i = 0; i < amountToTake; i++)
            {
                _payloadBuilder.Append(_sendBuffer.Dequeue());
                _payloadBuilder.Append("\n");
            }
            _attempt = 0;

            if (_payloadBuilder.Length > _charBuffer.Length)
            {
                _charBuffer = new char[_payloadBuilder.Length + 1024];
            }

            _payloadBuilder.CopyTo(0, _charBuffer, 0, _payloadBuilder.Length);
            _data = Encoding.UTF8.GetBytes(_charBuffer, 0, _payloadBuilder.Length);

            _uri = _metricsLogger.BaseUri;
            _payloadBuilder.Clear();
            yield return SendRequest();
        }
        _isRunning = false;
    }

    private IEnumerator SendRequest()
    {
        using (var request = new UnityWebRequest(_uri, UnityWebRequest.kHttpVerbPOST))
        {
            request.uploadHandler = new UploadHandlerRaw(_data);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = RequestTimeoutSeconds;
            request.useHttpContinue = false;
            request.redirectLimit = 0;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                if (_offlineLogged)
                {
                    Debug.Log("[HarmonyMetrics] InfluxDB uploads resumed");
                    _offlineLogged = false;
                }
                yield break;
            }

            if (request.result == UnityWebRequest.Result.ConnectionError)
            {
                if (_attempt >= 1)
                {
                    EnterOfflineMode(request.error);
                    yield break;
                }

                _attempt++;
                yield return SendRequest();
                yield break;
            }

            if (request.result == UnityWebRequest.Result.ProtocolError)
            {
                if (_throttleHttpErrorMessages)
                {
                    _accumulatedHttpErrors += 1;
                }
                else
                {
                    Debug.LogError("[HarmonyMetrics] HTTP error submitting metrics: " + request.error);
                    if (_metricsLogger.Configuration != null && _metricsLogger.Configuration.DebugLogging)
                    {
                        Debug.LogError(request.downloadHandler.text);
                    }
                    InvokeHandler.Invoke(this, _notifySubsequentHttpFailuresAction, 5);
                    _throttleHttpErrorMessages = true;
                }
            }
        }
    }

    private void EnterOfflineMode(string error)
    {
        _offlineUntil = Time.realtimeSinceStartup + OfflineBackoffSeconds;
        _sendBuffer.Clear();

        if (!_offlineLogged)
        {
            _offlineLogged = true;
            Debug.LogWarning("[HarmonyMetrics] InfluxDB unreachable (" + (error ?? "connection error") + "). Pausing uploads for " + OfflineBackoffSeconds + "s. Start Influx at http://127.0.0.1:8086");
        }
        else if (!_throttleNetworkErrorMessages)
        {
            Debug.LogWarning("[HarmonyMetrics] InfluxDB still unreachable; uploads remain paused");
            InvokeHandler.Invoke(this, _notifySubsequentNetworkFailuresAction, OfflineBackoffSeconds);
            _throttleNetworkErrorMessages = true;
        }
        else
        {
            _accumulatedNetworkErrors += 1;
        }
    }

    private void NotifySubsequentNetworkFailures()
    {
        _throttleNetworkErrorMessages = false;
        if (_accumulatedNetworkErrors == 0) return;
        Debug.LogWarning("[HarmonyMetrics] " + _accumulatedNetworkErrors + " additional Influx connection failures while paused");
        _accumulatedNetworkErrors = 0;
    }

    private void NotifySubsequentHttpFailures()
    {
        _throttleHttpErrorMessages = false;
        if (_accumulatedHttpErrors == 0) return;
        Debug.LogError("[HarmonyMetrics] " + _accumulatedHttpErrors + " subsequent HTTP errors occurred in the last 5 seconds");
        _accumulatedHttpErrors = 0;
    }

    private void OnDestroy()
    {
        Stop();
    }

    public void Stop()
    {
        _isRunning = false;
        _sendBuffer.Clear();
        StopAllCoroutines();
    }
}
