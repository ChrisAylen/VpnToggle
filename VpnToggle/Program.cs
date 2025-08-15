using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Forms;

namespace VpnToggle
{
    public class MainForm : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;

        // Config — defaults from your earlier notes
        private readonly string vpnGateway = "10.0.0.9";   // Unraid VPN gateway
        private readonly string normalDns = "10.0.0.1";    // pfSense
        private readonly string vpnDns = "10.64.0.1";   // Mullvad DNS
        private readonly int vpnMetric = 1;

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

            trayIcon.DoubleClick += ToggleVpn;
            UpdateStatusIcon();

            var t = new System.Windows.Forms.Timer { Interval = 3000 };
            t.Tick += (_, __) => UpdateStatusIcon();
            t.Start();
        }

        private void UpdateStatusIcon()
        {
            trayIcon.Text = IsVpnOverridePresent() ? "VPN Active" : "Normal Routing";
        }

        private (string ifaceAlias, int ifIndex) GetPrimaryInterface()
        {
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
                    var idx = props.GetIPv4Properties()?.Index ?? -1;
                    if (ipv4 != null && gw4 != null && idx > 0)
                        return (nic.Name, idx);
                }
                catch { }
            }
            throw new InvalidOperationException("No active IPv4 interface with a gateway was found.");
        }

        private bool IsVpnOverridePresent()
        {
            var output = RunExe("route", "print 0.0.0.0", out _);
            return output.Contains(vpnGateway);
        }

        private void ToggleVpn(object? sender, EventArgs e)
        {
            try
            {
                var (ifaceAlias, ifIndex) = GetPrimaryInterface();
                var toVpn = !IsVpnOverridePresent();

                if (toVpn)
                {
                    // Remove old split routes if present
                    RunExe("route", $"DELETE 0.0.0.0 MASK 128.0.0.0 {vpnGateway}", out _, ignoreExitCode: true);
                    RunExe("route", $"DELETE 128.0.0.0 MASK 128.0.0.0 {vpnGateway}", out _, ignoreExitCode: true);

                    // Add split defaults via VPN gateway
                    RunExe("route", $"ADD 0.0.0.0 MASK 128.0.0.0 {vpnGateway} METRIC {vpnMetric} IF {ifIndex}", out _, ignoreExitCode: false);
                    RunExe("route", $"ADD 128.0.0.0 MASK 128.0.0.0 {vpnGateway} METRIC {vpnMetric} IF {ifIndex}", out _, ignoreExitCode: false);

                    // Switch DNS to Mullvad
                    RunExe("netsh", $"interface ip set dnsservers name=\"{ifaceAlias}\" static {vpnDns} primary", out _);

                    trayIcon.ShowBalloonTip(2000, "VPN Toggle", "Switched to VPN routing.", ToolTipIcon.Info);
                }
                else
                {
                    // Remove split routes to return to normal default
                    RunExe("route", $"DELETE 0.0.0.0 MASK 128.0.0.0 {vpnGateway}", out _, ignoreExitCode: true);
                    RunExe("route", $"DELETE 128.0.0.0 MASK 128.0.0.0 {vpnGateway}", out _, ignoreExitCode: true);

                    // Switch DNS back to pfSense
                    RunExe("netsh", $"interface ip set dnsservers name=\"{ifaceAlias}\" static {normalDns} primary", out _);

                    trayIcon.ShowBalloonTip(2000, "VPN Toggle", "Switched to normal routing.", ToolTipIcon.Info);
                }



                UpdateStatusIcon();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "VPN Toggle error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ShowStatus()
        {
            var (iface, _) = GetPrimaryInterface();
            var mode = IsVpnOverridePresent() ? "VPN" : "Normal";
            MessageBox.Show($"Interface: {iface}\nMode: {mode}\nVPN GW override present: {IsVpnOverridePresent()}",
                "VPN Status", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string RunExe(string file, string args, out int exitCode, bool ignoreExitCode = false)
        {
            var psi = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Verb = "runas"
            };
            using var p = Process.Start(psi);
            var sb = new StringBuilder();
            if (p != null)
            {
                sb.Append(p.StandardOutput.ReadToEnd());
                sb.Append(p.StandardError.ReadToEnd());
                p.WaitForExit();
                exitCode = p.ExitCode;
                if (exitCode != 0 && !ignoreExitCode)
                    throw new InvalidOperationException($"{file} {args}\n\nExit {exitCode}\n\n{sb}");
                return sb.ToString();
            }
            exitCode = -1;
            if (!ignoreExitCode) throw new InvalidOperationException($"Failed to start {file} {args}");
            return "";
        }

        [STAThread]
        public static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }
}
