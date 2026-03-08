using System;
using System.IO;
using Newtonsoft.Json;

namespace LiveSplit.GW2
{
    public static class SplitConfigLoader
    {
        public static SplitConfigRoot Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Could not find split config JSON.", path);

            string json = File.ReadAllText(path);

            SplitConfigRoot config = JsonConvert.DeserializeObject<SplitConfigRoot>(json);

            if (config == null)
                throw new InvalidOperationException("Failed to parse split config JSON.");

            if (config.Splits == null)
                throw new InvalidOperationException("Config is missing 'splits'.");

            return config;
        }
    }
}