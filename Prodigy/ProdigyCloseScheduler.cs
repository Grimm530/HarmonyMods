using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Prodigy;

/// <summary>
/// Schedules closing the Prodigy UI panel after a delay using a coroutine (reliable on server).
/// </summary>
public class ProdigyCloseScheduler : MonoBehaviour
{
    private readonly Dictionary<ulong, Coroutine> _scheduled = new();

    public void ScheduleClose(ulong userId, float delaySeconds)
    {
        if (_scheduled.TryGetValue(userId, out var existing))
        {
            StopCoroutine(existing);
            _scheduled.Remove(userId);
        }
        var co = StartCoroutine(CloseAfter(userId, delaySeconds));
        _scheduled[userId] = co;
    }

    public void Cancel(ulong userId)
    {
        if (_scheduled.TryGetValue(userId, out var existing))
        {
            StopCoroutine(existing);
            _scheduled.Remove(userId);
        }
    }

    private IEnumerator CloseAfter(ulong userId, float delay)
    {
        yield return new WaitForSeconds(delay);
        _scheduled.Remove(userId);
        var player = BasePlayer.FindByID(userId);
        if (player != null && player.IsConnected)
            ProdigyUI.Destroy(player);
    }
}
