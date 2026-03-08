using System.Collections.Generic;
using Newtonsoft.Json;

namespace LiveSplit.GW2
{
    public class SplitConfigRoot
    {
        [JsonProperty("runName")]
        public string RunName { get; set; }

        [JsonProperty("start")]
        public StartConfig Start { get; set; }

        [JsonProperty("maps")]
        public List<uint> Maps { get; set; }

        [JsonProperty("splits")]
        public List<SplitConfig> Splits { get; set; }
    }

    public class StartConfig
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("mapId")]
        public uint MapId { get; set; }
    }

    public class SplitConfig
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("primary")]
        public TriggerConfig Primary { get; set; }

        [JsonProperty("fallback")]
        public TriggerConfig Fallback { get; set; }
    }

    public class TriggerConfig
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("mapId")]
        public uint? MapId { get; set; }

        [JsonProperty("x")]
        public float? X { get; set; }

        [JsonProperty("z")]
        public float? Z { get; set; }

        [JsonProperty("radius")]
        public float? Radius { get; set; }

        [JsonProperty("boss")]
        public string Boss { get; set; }

        [JsonProperty("points")]
        public List<TriggerPointConfig> Points { get; set; }
    }

    public class TriggerPointConfig
    {
        [JsonProperty("x")]
        public float? X { get; set; }

        [JsonProperty("z")]
        public float? Z { get; set; }
    }
}