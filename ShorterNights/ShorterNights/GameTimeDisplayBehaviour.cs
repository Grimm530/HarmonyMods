using System.Collections;
using UnityEngine;

namespace ShorterNights;

public class GameTimeDisplayBehaviour : MonoBehaviour
{
    private void Start()
    {
        StartCoroutine(RefreshLoop());
    }

    private IEnumerator RefreshLoop()
    {
        yield return null;
        yield return null;
        for (int i = 0; i < 5; i++)
        {
            GameTimeDisplayUI.RefreshAll();
            yield return new WaitForSeconds(1f);
        }
        while (this != null)
        {
            yield return new WaitForSeconds(1f);
            GameTimeDisplayUI.RefreshAll();
        }
    }
}
