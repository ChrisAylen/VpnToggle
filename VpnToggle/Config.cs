using System;
using System.IO;
using System.Text.Json;

namespace VpnToggle
{
    public class Config
    {
        public string InterfaceName { get; set; } = "vEthernet (External LAN)"; // change on first run if needed
        public string VpnGateway { get; set; } = "10.0.0.9";
        public string NormalGateway { get; set; } = "10.0.0.1"; // informational only (we don’t delete it)
        public string VpnDns { get; set; } = "10.64.0.1";
        public string NormalDns { get; set; } = "10.0.0.1"; // leave empty to reset to DHCP

        public static string Path =>
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VpnSwitcher", "config.json");

        public static Config LoadOrCreate()
        {
            try
            {
                if (File.Exists(Path))
                {
                    var json = File.ReadAllText(Path);
                    var cfg = JsonSerializer.Deserialize<Config>(json);
                    if (cfg != null) return cfg;
                }
            }
            catch { /* ignore */ }

            var def = new Config();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(def, new JsonSerializerOptions { WriteIndented = true }));
            return def;
        }

        public void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
