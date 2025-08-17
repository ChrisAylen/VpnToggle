using System;
using System.IO;
using System.Text.Json;

namespace VpnToggle
{
    public class Config
    {
        public string InterfaceName { get; set; } = "Ethernet";
        public string VpnGateway { get; set; } = "10.0.0.9";
        public string NormalDns { get; set; } = "10.0.0.1";
        public string VpnDns { get; set; } = "10.64.0.1";
        public int VpnMetric { get; set; } = 1;
        public bool LastKnownVpnState { get; set; } = false;

        // Change to match AppConfig paths:
        public static string ConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "VpnToggle", "config.json"); // Changed from settings.json

        // Update Load() to match AppConfig's pattern:
        public static Config Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    var cfg = JsonSerializer.Deserialize<Config>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch { /* use defaults */ }
            return new Config().Save(); // Chain save like AppConfig
        }

        public Config Save() // Return this for chaining
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
            return this;
        }
    }
}
