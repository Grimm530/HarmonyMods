using UnityEngine;

namespace Prodigy;

public class ProdigyTickBehaviour : MonoBehaviour
{
    private void Update()
    {
        ProdigyMod.Instance?.Update(Time.deltaTime);
    }
}
