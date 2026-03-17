using System.Collections.Generic;
using Newtonsoft.Json;

namespace LiveSplit.GW2
{
    public class FullWingConfigRoot
    {
        [JsonProperty("mapId")]
        public uint MapId { get; set; }

        [JsonProperty("splits")]
        public List<SplitConfig> Splits { get; set; }
    }

    public class EncounterConfigRoot
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("mapId")]
        public uint MapId { get; set; }

        [JsonProperty("splits")]
        public List<SplitConfig> Splits { get; set; }
    }

    public class RouteConfigRoot
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("encounters")]
        public List<string> Encounters { get; set; }
    }

    public class SplitConfig
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("trigger")]
        public TriggerConfig Trigger { get; set; }

        [JsonProperty("ORtrigger")]
        public List<TriggerConfig> OrTrigger { get; set; }

        [JsonProperty("or")]
        private List<TriggerConfig> LegacyOrTrigger
        {
            set
            {
                if (OrTrigger == null || OrTrigger.Count == 0)
                    OrTrigger = value;
            }
        }
    }

    public class TriggerConfig
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("mapId")]
        public uint? MapId { get; set; }

        [JsonProperty("x")]
        public float? X { get; set; }

        [JsonProperty("y")]
        public float? Y { get; set; }

        [JsonProperty("z")]
        public float? Z { get; set; }

        [JsonProperty("radius")]
        public float? Radius { get; set; }

        [JsonProperty("boss")]
        public string Boss { get; set; }

        [JsonProperty("points")]
        public List<TriggerPointConfig> Points { get; set; }

        [JsonProperty("combatState")]
        public string CombatState { get; set; }

        [JsonProperty("yAbove")]
        public float? YAbove { get; set; }

        [JsonProperty("yBelow")]
        public float? YBelow { get; set; }
    }

    public class TriggerPointConfig
    {
        [JsonProperty("x")]
        public float? X { get; set; }

        [JsonProperty("y")]
        public float? Y { get; set; }

        [JsonProperty("z")]
        public float? Z { get; set; }
    }
}
