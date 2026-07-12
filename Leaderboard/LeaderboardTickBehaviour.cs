using UnityEngine;

namespace Leaderboard;

public class LeaderboardTickBehaviour : MonoBehaviour
{
    private void Update()
    {
        LeaderboardMod.Instance?.Update(Time.deltaTime);
    }
}
