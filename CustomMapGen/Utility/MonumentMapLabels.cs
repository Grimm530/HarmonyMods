using System;
using System.Reflection;
using UnityEngine;

namespace CustomMapGen.Utility
{
    /// <summary>In-game monument names for the generated map PNG (not prefab tokens like RADTOWN SMALL 3).</summary>
    internal static class MonumentMapLabels
    {
        public struct Label
        {
            public string Text;
            public int Rank;
        }

        public static bool TryGet(MonumentInfo monument, out Label label)
        {
            label = default;
            if (monument == null)
                return false;

            string path = monument.name ?? "";
            string pathLower = path.ToLowerInvariant();
            if (ShouldSkip(pathLower, monument))
                return false;

            if (!monument.shouldDisplayOnMap)
                return false;

            string mapped = MapPrefabName(pathLower, out int rank);
            if (string.IsNullOrEmpty(mapped))
                mapped = CleanPhrase(GetDisplayPhrase(monument), pathLower, out rank);
            if (string.IsNullOrEmpty(mapped))
                return false;

            label = new Label { Text = mapped, Rank = rank };
            return true;
        }

        static bool ShouldSkip(string pathLower, MonumentInfo monument)
        {
            if (monument.mapIcon != null && pathLower.IndexOf("oil", StringComparison.Ordinal) < 0)
                return true;
            if (pathLower.IndexOf("power_sub", StringComparison.Ordinal) >= 0)
                return true;
            if (pathLower.IndexOf("train_tunnel", StringComparison.Ordinal) >= 0
                || pathLower.IndexOf("tunnel-entrance", StringComparison.Ordinal) >= 0
                || pathLower.IndexOf("tunnel_entrance", StringComparison.Ordinal) >= 0
                || pathLower.IndexOf("entrance_monuments", StringComparison.Ordinal) >= 0)
                return true;
            if (monument.MapLayer != MapLayer.Overworld && pathLower.IndexOf("oil", StringComparison.Ordinal) < 0
                && pathLower.IndexOf("underwater", StringComparison.Ordinal) < 0)
                return true;
            return false;
        }

        static string GetDisplayPhrase(MonumentInfo monument)
        {
            try
            {
                var phraseProp = monument.GetType().GetProperty("displayPhrase");
                object phrase = phraseProp?.GetValue(monument);
                if (phrase == null)
                    return null;
                var enProp = phrase.GetType().GetProperty("english");
                string en = enProp?.GetValue(phrase) as string;
                if (!string.IsNullOrEmpty(en))
                    return en.Replace("\n", " ").Trim();
            }
            catch { }
            return null;
        }

        static string MapPrefabName(string pathLower, out int rank)
        {
            rank = 1;
            if (Contains(pathLower, "radtown_small")) { rank = 2; return "Sewer Branch"; }
            if (Contains(pathLower, "compound") || Contains(pathLower, "outpost")) { rank = 2; return "Outpost"; }
            if (Contains(pathLower, "bandit")) { rank = 2; return "Bandit Camp"; }
            if (Contains(pathLower, "oilrig_1") || Contains(pathLower, "oil_rig_a") || Contains(pathLower, "large_oil")) { rank = 2; return "Large Oil Rig"; }
            if (Contains(pathLower, "oilrig_2") || Contains(pathLower, "oil_rig_b") || Contains(pathLower, "small_oil")) { rank = 2; return "Small Oil Rig"; }
            if (Contains(pathLower, "oilrig") || Contains(pathLower, "oil_rig")) { rank = 2; return "Oil Rig"; }
            if (Contains(pathLower, "launch_site") || Contains(pathLower, "launchsite")) { rank = 3; return "Launch Site"; }
            if (Contains(pathLower, "water_treatment")) { rank = 2; return "Water Treatment"; }
            if (Contains(pathLower, "military_tunnel")) { rank = 2; return "Military Tunnels"; }
            if (Contains(pathLower, "trainyard")) { rank = 2; return "Train Yard"; }
            if (Contains(pathLower, "powerplant") || Contains(pathLower, "power_plant")) { rank = 2; return "Power Plant"; }
            if (Contains(pathLower, "sphere_tank")) { rank = 2; return "The Dome"; }
            if (Contains(pathLower, "satellite")) { rank = 2; return "Satellite Dish"; }
            if (Contains(pathLower, "excavator")) { rank = 2; return "Giant Excavator"; }
            if (Contains(pathLower, "missile_silo") || Contains(pathLower, "nuclear_missile")) { rank = 3; return "Missile Silo"; }
            if (Contains(pathLower, "airfield")) { rank = 2; return "Airfield"; }
            if (Contains(pathLower, "junkyard")) { rank = 2; return "Junkyard"; }
            if (Contains(pathLower, "ferry_terminal")) { rank = 2; return "Ferry Terminal"; }
            if (Contains(pathLower, "stables_a")) { rank = 1; return "Ranch"; }
            if (Contains(pathLower, "stables_b")) { rank = 1; return "Large Barn"; }
            if (Contains(pathLower, "mining_quarry_a")) { rank = 1; return "Sulfur Quarry"; }
            if (Contains(pathLower, "mining_quarry_b")) { rank = 1; return "Stone Quarry"; }
            if (Contains(pathLower, "mining_quarry_c")) { rank = 1; return "HQM Quarry"; }
            if (Contains(pathLower, "gas_station")) { rank = 0; return "Gas Station"; }
            if (Contains(pathLower, "supermarket")) { rank = 0; return "Supermarket"; }
            if (Contains(pathLower, "warehouse")) { rank = 0; return "Warehouse"; }
            if (Contains(pathLower, "lighthouse")) { rank = 1; return "Lighthouse"; }
            if (Contains(pathLower, "harbor")) { rank = 2; return WithVariant("Harbor", pathLower); }
            if (Contains(pathLower, "fishing_village")) { rank = 1; return WithLetter("Fishing Village", pathLower); }
            if (Contains(pathLower, "underwater_lab")) { rank = 2; return WithLetter("Underwater Lab", pathLower); }
            if (Contains(pathLower, "arctic_research") || Contains(pathLower, "arctic_base")) { rank = 2; return WithLetter("Arctic Base", pathLower); }
            if (Contains(pathLower, "desert_military") || Contains(pathLower, "desert_base")) { rank = 2; return WithLetter("Desert Base", pathLower); }
            if (Contains(pathLower, "jungle_ziggurat") || Contains(pathLower, "ziggurat")) { rank = 2; return WithLetter("Ziggurat", pathLower); }
            if (Contains(pathLower, "swamp")) { rank = 1; return WithLetter("Swamp", pathLower); }
            if (Contains(pathLower, "radtown")) { rank = 2; return "Radtown"; }
            if (Contains(pathLower, "apartment")) { rank = 1; return "Abandoned Apartments"; }
            if (Contains(pathLower, "iceberg")) { rank = 0; return "Iceberg"; }
            return null;
        }

        static string CleanPhrase(string phrase, string pathLower, out int rank)
        {
            rank = Contains(pathLower, "xlarge") || Contains(pathLower, "launch") ? 3
                : Contains(pathLower, "/large") || Contains(pathLower, "monument/large") ? 2
                : Contains(pathLower, "roadside") || Contains(pathLower, "/tiny") ? 0
                : 1;
            if (string.IsNullOrEmpty(phrase))
                return null;
            string titled = TitleCase(phrase);
            if (titled.EndsWith(" 1", StringComparison.Ordinal))
                titled = titled.Substring(0, titled.Length - 2);
            titled = titled
                .Replace("Radtown Small 3", "Sewer Branch")
                .Replace("Sphere Tank", "The Dome")
                .Replace("Oilrig 1", "Large Oil Rig")
                .Replace("Oilrig 2", "Small Oil Rig")
                .Replace("Compound", "Outpost")
                .Replace("Military Tunnel 1", "Military Tunnels")
                .Replace("Water Treatment Plant", "Water Treatment")
                .Replace("Nuclear Missile Silo", "Missile Silo")
                .Replace("Train Tunnel Double Entrance", "")
                .Replace("Train Tunnel Entrance", "");
            return string.IsNullOrWhiteSpace(titled) ? null : titled.Trim();
        }

        static string WithLetter(string baseName, string pathLower)
        {
            char letter = ExtractLetter(pathLower);
            return letter == '\0' ? baseName : baseName + " " + letter;
        }

        static string WithVariant(string baseName, string pathLower)
        {
            if (pathLower.IndexOf("harbor_1", StringComparison.Ordinal) >= 0 || pathLower.IndexOf("harbor_a", StringComparison.Ordinal) >= 0)
                return baseName + " 1";
            if (pathLower.IndexOf("harbor_2", StringComparison.Ordinal) >= 0 || pathLower.IndexOf("harbor_b", StringComparison.Ordinal) >= 0)
                return baseName + " 2";
            return baseName;
        }

        static char ExtractLetter(string pathLower)
        {
            int prefab = pathLower.LastIndexOf('/');
            string file = prefab >= 0 ? pathLower.Substring(prefab + 1) : pathLower;
            for (int i = file.Length - 1; i >= 0; i--)
            {
                char c = file[i];
                if (c >= 'a' && c <= 'c' && (i == 0 || file[i - 1] == '_' || file[i - 1] == ' '))
                    return char.ToUpperInvariant(c);
            }
            return '\0';
        }

        static string TitleCase(string s)
        {
            var parts = s.ToLowerInvariant().Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 1)
                {
                    parts[i] = parts[i].ToUpperInvariant();
                    continue;
                }
                parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
            }
            return string.Join(" ", parts);
        }

        static bool Contains(string hay, string needle) =>
            hay.IndexOf(needle, StringComparison.Ordinal) >= 0;

        public static int FontSize(int rank, float imageScale)
        {
            int px = rank >= 3 ? 26 : rank == 2 ? 20 : rank == 1 ? 16 : 13;
            return Mathf.Max(11, Mathf.RoundToInt(px * imageScale));
        }
    }
}
