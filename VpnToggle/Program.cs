using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Forms;

namespace VpnToggle
{
    public class MainForm : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;

        // Your endpoints
        private readonly string vpnGateway = "10.0.0.9";   // Unraid VPN gateway
        private readonly string normalGateway = "10.0.0.1";   // pfSense
        private readonly string vpnDns = "10.64.0.1";  // Mullvad DNS
        private readonly string normalDns = "10.0.0.1";   // Your LAN DNS (pfSense)

        public MainForm()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Toggle VPN", null, ToggleVpn);
            trayMenu.Items.Add("Show Status", null, (s, e) => ShowStatus());
            trayMenu.Items.Add("Exit", null, (s, e) => Application.Exit());

            trayIcon = new NotifyIcon
            {
                Text = "VPN Toggle",
                Icon = System.Drawing.SystemIcons.Application,
                ContextMenuStrip = trayMenu,
                Visible = true
            };

            UpdateStatusIcon();
        }

        private void UpdateStatusIcon()
        {
            trayIcon.Text = IsVpnActive() ? "VPN Active" : "Normal Routing";
        }

        private bool IsVpnActive()
        {
            var (ifaceName, currentGw) = GetDefaultInterfaceAndGateway();
            return !string.IsNullOrEmpty(currentGw) && currentGw == vpnGateway;
        }

        private (string ifaceName, string gateway) GetDefaultInterfaceAndGateway()
        {
            // Pick an UP, non-loopback, IPv4 adapter that has a gateway
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up
                                  && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                  && n.Supports(NetworkInterfaceComponent.IPv4)))
            {
                try
                {
                    var props = nic.GetIPProperties();
                    var ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    var gw4 = props.GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (ipv4 != null && gw4 != null)
                        return (nic.Name, gw4.Address.ToString()); // nic.Name == interface alias used by netsh
                }
                catch { /* ignore and continue */ }
            }
            return (null, null);
        }

        private void ToggleVpn(object sender, EventArgs e)
        {
            var (iface, currentGw) = GetDefaultInterfaceAndGateway();
            if (string.IsNullOrEmpty(iface))
            {
                MessageBox.Show("No active IPv4 interface with a gateway was found.", "VPN Toggle", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            bool toVpn = currentGw != vpnGateway;

            // 1) Delete ALL existing default routes on this interface
            RunNetsh($"interface ipv4 delete route 0.0.0.0/0 name=\"{iface}\"", requireSuccess: false); // ok if none

            // 2) Add the desired default route
            string nextHop = toVpn ? vpnGateway : normalGateway;
            if (!RunNetsh($"interface ipv4 add route 0.0.0.0/0 name=\"{iface}\" {nextHop} metric=5 store=active", out var addOut))
            {
                MessageBox.Show($"Failed to set default route via {nextHop} on \"{iface}\".\n\n{addOut}", "VPN Toggle", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // 3) Set DNS accordingly
            if (toVpn)
            {
                // Mullvad DNS
                RunNetsh($"interface ipv4 set dnsservers name=\"{iface}\" static {vpnDns} primary");
            }
            else
            {
                // Back to pfSense (or use 'dhcp' if you prefer)
                RunNetsh($"interface ipv4 set dnsservers name=\"{iface}\" static {normalDns} primary");
            }

            // 4) Quick verification
            var (_, newGw) = GetDefaultInterfaceAndGateway();
            var mode = toVpn ? "VPN" : "Normal";
            if (newGw == nextHop)
                MessageBox.Show($"Switched to {mode} routing via {nextHop} on \"{iface}\".", "VPN Toggle");
            else
                MessageBox.Show($"Tried to switch to {mode} routing, but gateway is still {newGw ?? "(none)"}.\nCheck permissions and interface name.", "VPN Toggle", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            UpdateStatusIcon();
        }

        private void ShowStatus()
        {
            var (iface, gw) = GetDefaultInterfaceAndGateway();
            MessageBox.Show($"Interface: {iface ?? "(none)"}\nGateway: {gw ?? "(none)"}\nMode: {(gw == vpnGateway ? "VPN" : "Normal")}", "VPN Status");
        }

        private bool RunNetsh(string args, out string output)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            output = "";
            if (p != null)
            {
                var sb = new StringBuilder();
                sb.AppendLine(p.StandardOutput.ReadToEnd());
                sb.AppendLine(p.StandardError.ReadToEnd());
                p.WaitForExit();
                output = sb.ToString().Trim();
                return p.ExitCode == 0;
            }
            return false;
        }

        private void RunNetsh(string args, bool requireSuccess = true)
        {
            if (!RunNetsh(args, out var output) && requireSuccess)
                MessageBox.Show(output, "netsh error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
