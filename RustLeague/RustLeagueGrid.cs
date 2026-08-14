using System.Collections.Generic;
using UnityEngine;

namespace RustLeagueHarmony
{
    /// <summary>
    /// Sky placement: random map XZ, no terrain/monument scan.
    /// Height is applied by <see cref="RustLeaguePlugin.LiftToSky"/>.
    /// </summary>
    public sealed class RustLeagueGrid
    {
        private readonly RustLeaguePlugin _plugin;

        public IReadOnlyList<Vector3> Spawns => System.Array.Empty<Vector3>();
        public bool IsScanning => false;
        public bool Ready => true;

        public RustLeagueGrid(RustLeaguePlugin plugin)
        {
            _plugin = plugin;
        }

        public void StartScan() { }

        public void StopScan() { }

        public bool TryPick(out Vector3 position)
        {
            position = _plugin.PickRandomSkyOrigin();
            return position != Vector3.zero;
        }
    }
}
