using UnityEngine;

namespace RustEditStandalone.Components;

/// <summary>
/// Bridges IO power flags onto WheelSwitch-style powered pulsing.
/// </summary>
public sealed class IoToWheelSwitch : MonoBehaviour
{
    private IOEntity _entity;
    private bool _wasOn;

    private void Awake()
    {
        _entity = GetComponent<IOEntity>();
    }

    private void OnEnable()
    {
        CancelInvoke(nameof(Tick));
        InvokeRepeating(nameof(Tick), 0.1f, 0.1f);
    }

    private void OnDisable()
    {
        CancelInvoke(nameof(Tick));
        CancelInvoke(nameof(Powered));
    }

    private void Tick()
    {
        if (_entity == null)
        {
            Destroy(this);
            return;
        }

        bool on = _entity.HasFlag(BaseEntity.Flags.On) || _entity.HasFlag(BaseEntity.Flags.Reserved1);
        if (on && !_wasOn)
        {
            CancelInvoke(nameof(Powered));
            InvokeRepeating(nameof(Powered), 0f, 0.1f);
        }
        else if (!on && _wasOn)
        {
            CancelInvoke(nameof(Powered));
        }
        _wasOn = on;
    }

    private void Powered()
    {
        if (_entity == null) return;
        _entity.SendChangedToRoot(forceUpdate: true);
    }
}
