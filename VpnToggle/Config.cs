using System;
using System.IO;
using System.Text.Json;

namespace VpnToggle
{
    public class Config
    {
        public string InterfaceName { get; set; } = "Ethernet"; // set "auto" to auto-detect
        public string VpnGateway { get; set; } = "10.0.0.9";  // Unraid wg NAT IP
        public string NormalDns { get; set; } = "10.0.0.1";  // pfSense
        public string VpnDns { get; set; } = "10.64.0.1"; // Mullvad DNS
        public int VpnMetric { get; set; } = 1;           // route metric for /1s

        public static string ConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "VpnToggle", "settings.json");

        public static Config Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<Config>(json) ?? new Config();
                }
            }
            catch { /* ignore and use defaults */ }
            return new Config();
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(ConfigPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
    }
}
