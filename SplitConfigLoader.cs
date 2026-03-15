using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace LiveSplit.GW2
{
    public static class SplitConfigLoader
    {
        public static Dictionary<uint, FullWingConfigRoot> LoadFullWings(string folder)
        {
            var result = new Dictionary<uint, FullWingConfigRoot>();

            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException("Fullwing config folder not found.");

            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                string json = File.ReadAllText(file);
                FullWingConfigRoot config = JsonConvert.DeserializeObject<FullWingConfigRoot>(json);

                if (config == null || config.Splits == null)
                    continue;

                result[config.MapId] = config;
            }

            return result;
        }

        public static Dictionary<string, EncounterConfigRoot> LoadEncounters(string folder)
        {
            var result = new Dictionary<string, EncounterConfigRoot>(StringComparer.OrdinalIgnoreCase);

            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException("Encounter config folder not found.");

            foreach (string file in Directory.GetFiles(folder, "*.json"))
            {
                string json = File.ReadAllText(file);
                EncounterConfigRoot config = JsonConvert.DeserializeObject<EncounterConfigRoot>(json);

                if (config == null || string.IsNullOrWhiteSpace(config.Id) || config.Splits == null)
                    continue;

                result[config.Id] = config;
            }

            return result;
        }

        public static RouteConfigRoot LoadRoute(string path)
        {
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            RouteConfigRoot config = JsonConvert.DeserializeObject<RouteConfigRoot>(json);

            if (config == null || config.Encounters == null)
                return null;

            return config;
        }
    }
}