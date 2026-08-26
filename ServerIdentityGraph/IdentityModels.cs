using System.Collections.Generic;
using Newtonsoft.Json;

namespace ServerIdentityGraph
{
    public sealed class IdentityConfig
    {
        [JsonProperty("FlushSeconds")]
        public float FlushSeconds { get; set; } = 2f;
    }

    public sealed class PlayerIdentity
    {
        [JsonProperty("steamId")]
        public string SteamId { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("lastSeen")]
        public string LastSeen { get; set; } = "";

        [JsonProperty("team")]
        public TeamSighting Team { get; set; }

        [JsonProperty("clan")]
        public ClanSighting Clan { get; set; }
    }

    public sealed class MemberSighting
    {
        [JsonProperty("steamId")]
        public string SteamId { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";
    }

    public sealed class TeamSighting
    {
        [JsonProperty("teamId")]
        public string TeamId { get; set; } = "";

        [JsonProperty("leader")]
        public MemberSighting Leader { get; set; }

        [JsonProperty("members")]
        public List<MemberSighting> Members { get; set; } = new List<MemberSighting>();

        [JsonProperty("seenAt")]
        public string SeenAt { get; set; } = "";
    }

    public sealed class ClanSighting
    {
        [JsonProperty("clanId")]
        public string ClanId { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("members")]
        public List<MemberSighting> Members { get; set; } = new List<MemberSighting>();

        [JsonProperty("seenAt")]
        public string SeenAt { get; set; } = "";
    }
}
