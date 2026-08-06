using UnityEngine;

namespace RustEditStandalone.Components;

public sealed class CardReaderMonitor : MonoBehaviour
{
    private CardReader _reader;
    private float _timerLength;

    private void Awake()
    {
        _reader = GetComponent<CardReader>();
    }

    public void Setup(float timerLength)
    {
        _timerLength = timerLength;
        if (_timerLength <= 0f)
        {
            Destroy(this);
            return;
        }
        CancelInvoke(nameof(Tick));
        InvokeRepeating(nameof(Tick), 0.25f, 0.25f);
    }

    private void Tick()
    {
        if (_reader == null)
        {
            Destroy(this);
            return;
        }
        if (_reader.HasFlag(BaseEntity.Flags.On))
        {
            CancelInvoke(nameof(Tick));
            Invoke(nameof(ResetIo), _timerLength);
        }
    }

    private void ResetIo()
    {
        if (_reader != null)
            _reader.SetFlag(BaseEntity.Flags.On, false);
        CancelInvoke(nameof(Tick));
        InvokeRepeating(nameof(Tick), 0.25f, 0.25f);
    }
}
