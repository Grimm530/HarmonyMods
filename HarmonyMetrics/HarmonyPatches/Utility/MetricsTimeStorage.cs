using System;
using System.Collections.Generic;
using System.Text;

namespace HarmonyMetrics.HarmonyPatches.Utility;

public class MetricsTimeStorage<TKey>
{
    private readonly string _metricKey;
    private readonly Action<StringBuilder, TKey> _stringBuilderSerializer;
    private readonly Dictionary<TKey, double> _dict = new Dictionary<TKey, double>();
    private readonly StringBuilder _sb = new StringBuilder();

    public MetricsTimeStorage(string metricKey, Action<StringBuilder, TKey> stringBuilderSerializer)
    {
        _metricKey = metricKey;
        _stringBuilderSerializer = stringBuilderSerializer;
    }

    public void LogTime(TKey key, double milliseconds)
    {
        if (!MetricsLogger.IsReady)
            return;

        double currentDuration;
        if (!_dict.TryGetValue(key, out currentDuration))
        {
            _dict.Add(key, milliseconds);
            return;
        }

        _dict[key] = currentDuration + milliseconds;
    }

    public void SerializeToStringBuilder()
    {
        if (!MetricsLogger.IsReady)
            return;

        var instance = MetricsLogger.Instance;
        if (instance == null || instance.Configuration == null)
            return;

        var serverTag = instance.Configuration.ServerTag;
        var epochNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (var item in _dict)
        {
            _sb.Clear();
            _sb.Append(_metricKey);
            _sb.Append(",server=");
            _sb.Append(serverTag);
            _stringBuilderSerializer.Invoke(_sb, item.Key);
            _sb.Append("\" duration=");
            _sb.Append((float)item.Value);
            _sb.Append(" ");
            _sb.Append(epochNow);
            instance.AddToSendBuffer(_sb.ToString());
        }

        _dict.Clear();
    }
}
