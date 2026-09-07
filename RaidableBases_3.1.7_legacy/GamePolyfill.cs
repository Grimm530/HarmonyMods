/*
 * Polyfill for game types that may be missing in some Rust/Unity builds.
 * Do not redefine types that exist in Assembly-CSharp (DeepSeaManager, PointEntity, StringView, etc.).
 */
namespace Steamworks
{
    public static class SteamInventory
    {
        public static int[] Definitions => null;
    }
}

namespace ConVar
{
    public static class ConsoleSystem
    {
        public enum Option { Server }
        public static void Run(Option opt, string command) { }
        public class Arg
        {
            public bool HasArgs() => false;
            public string[] Args => null;
            public BasePlayer Player() => null;
            public CVar cmd => null;
        }
        public class CVar
        {
            public string FullName => "";
        }
    }
}

namespace Rust
{
    public static class TerrainBiome
    {
        public enum Enum
        {
            Arid = 1, Arctic = 2, Temperate = 4, Tundra = 8, Jungle = 16
        }
    }
    public static class TerrainTopology
    {
        public const int EVERYTHING = -1;
        public enum Enum
        {
            Beach = 1, Beachside = 2, Building = 4, Monument = 8, Rail = 16, Railside = 32, River = 64, Riverside = 128, Road = 256, Roadside = 512
        }
    }
}
