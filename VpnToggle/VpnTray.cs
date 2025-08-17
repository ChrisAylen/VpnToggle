using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace VpnToggle
{
    /// <summary>Tray-only app context: toggle VPN split-routes, switch DNS, show status, edit settings.</summary>
    public class VpnTray : ApplicationContext
    {
        private readonly NotifyIcon trayIcon;
        private readonly ContextMenuStrip trayMenu;
        private readonly System.Windows.Forms.Timer statusTimer;

        private readonly Config cfg;

        private readonly Icon iconOn;   // green
        private readonly Icon iconOff;  // red

        public VpnTray()
        {
            // Load config (from %AppData%\VpnToggle\config.json)
            cfg = Config.Load();

            // Clean up any stray routes on startup
            RestoreLastKnownState();

            // Make simple nice-looking icons (no external files)
            iconOn = CreateCircleIcon(Color.FromArgb(24, 166, 84));  // green
            iconOff = CreateCircleIcon(Color.FromArgb(220, 53, 69));  // red

            // Tray menu
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Toggle VPN", null, (_, __) => ToggleVpn());
            trayMenu.Items.Add("Show Status", null, (_, __) => ShowStatus());
            trayMenu.Items.Add("Settings…", null, (_, __) => OpenSettings());
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add("Exit", null, (_, __) => ExitThread());

            // Tray icon
            trayIcon = new NotifyIcon
            {
                Text = "VPN Toggle",
                Icon = iconOff,
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            trayIcon.DoubleClick += (_, __) => ToggleVpn();

            // Periodic status refresh (in case routes/DNS change outside the app)
            statusTimer = new System.Windows.Forms.Timer { Interval = 3000 };
            statusTimer.Tick += (_, __) => UpdateTrayIcon();
            statusTimer.Start();

            UpdateTrayIcon();
        }

        protected override void ExitThreadCore()
        {
            statusTimer?.Stop();
            trayIcon.Visible = false;
            trayIcon.Dispose();
            iconOn?.Dispose();
            iconOff?.Dispose();
            base.ExitThreadCore();
        }

        // ========= Actions =========

        public void ToggleVpn()
        {
            try
            {
                var (ifaceAlias, ifIndex) = GetPrimaryInterface();
                bool toVpn = !IsVpnSplitRoutesPresent();

                if (toVpn)
                {
                    // Avoid “no internet” trap if VPN gateway not reachable
                    if (!PingHost(cfg.VpnGateway))
                    {
                        ShowBalloon("VPN Toggle",
                            $"VPN gateway {cfg.VpnGateway} not reachable – staying on normal routing.",
                            ToolTipIcon.Warning);
                        return;
                    }

                    // Clean prior split routes (ignore errors)
                    RunExe("route", $"DELETE 0.0.0.0 MASK 128.0.0.0 {cfg.VpnGateway}", out _, true);
                    RunExe("route", $"DELETE 128.0.0.0 MASK 128.0.0.0 {cfg.VpnGateway}", out _, true);

                    // Add split defaults (these outrank /0 via pfSense)
                    RunExe("route", $"ADD 0.0.0.0 MASK 128.0.0.0 {cfg.VpnGateway} METRIC {cfg.VpnMetric} IF {ifIndex}", out _);
                    RunExe("route", $"ADD 128.0.0.0 MASK 128.0.0.0 {cfg.VpnGateway} METRIC {cfg.VpnMetric} IF {ifIndex}", out _);

                    // DNS → Mullvad
                    RunExe("netsh", $"interface ip set dnsservers name=\"{ifaceAlias}\" static {cfg.VpnDns} primary", out _);
                    RunExe("ipconfig", "/flushdns", out _, true);

                    ShowBalloon("VPN Toggle", $"Switched to VPN via {cfg.VpnGateway} on \"{ifaceAlias}\".");

                    cfg.LastKnownVpnState = true; // or false for off
                    cfg.Save();
                }
                else
                {
                    // Remove both split routes
                    RunExe("route", $"DELETE 0.0.0.0 MASK 128.0.0.0 {cfg.VpnGateway}", out _, true);
                    RunExe("route", $"DELETE 128.0.0.0 MASK 128.0.0.0 {cfg.VpnGateway}", out _, true);
                    // Also remove any stray /0 via VPN GW
                    RunExe("route", $"DELETE 0.0.0.0 MASK 0.0.0.0 {cfg.VpnGateway}", out _, true);

                    // DNS → pfSense (or your normal DNS)
                    var (ifaceAlias2, _) = GetPrimaryInterface(); // re-resolve in case alias changed
                    RunExe("netsh", $"interface ip set dnsservers name=\"{ifaceAlias2}\" static {cfg.NormalDns} primary", out _);
                    RunExe("ipconfig", "/flushdns", out _, true);

                    ShowBalloon("VPN Toggle", "Switched to normal routing.");

                    cfg.LastKnownVpnState = false; // or false for off
                    cfg.Save();
                }

                UpdateTrayIcon();
            }
            catch (Exception ex)
            {
                ShowBalloon("VPN Toggle Error", ex.Message, ToolTipIcon.Error, 5000);
            }
        }

        private void ShowStatus()
        {
            try
            {
                var (ifaceAlias, _) = GetPrimaryInterface();
                bool vpn = IsVpnSplitRoutesPresent();
                ShowBalloon("VPN Status",
                    $"Interface: {ifaceAlias}\nMode: {(vpn ? "VPN" : "Normal")}",
                    vpn ? ToolTipIcon.Info : ToolTipIcon.None,
                    3000);
            }
            catch (Exception ex)
            {
                ShowBalloon("VPN Status", ex.Message, ToolTipIcon.Error, 4000);
            }
        }

        private void OpenSettings()
        {
            using var dlg = new SettingsForm(cfg);
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                // Save + in-memory update
                cfg.Save();
                ShowBalloon("VPN Toggle", "Settings saved.", ToolTipIcon.Info);
                UpdateTrayIcon();
            }
        }

        private void UpdateTrayIcon()
        {
            bool vpn = IsVpnSplitRoutesPresent();
            trayIcon.Icon = vpn ? iconOn : iconOff;
            trayIcon.Text = vpn ? "VPN Active" : "Normal Routing";
        }

        private void RestoreLastKnownState()
        {
            try
            {
                bool currentVpnState = IsVpnSplitRoutesPresent();
                bool intendedVpnState = cfg.LastKnownVpnState;

                if (currentVpnState != intendedVpnState)
                {
                    if (intendedVpnState && PingHost(cfg.VpnGateway))
                    {
                        // Restore VPN state
                        var (ifaceAlias, ifIndex) = GetPrimaryInterface();
                        RunExe("route", $"ADD 0.0.0.0 MASK 128.0.0.0 {cfg.VpnGateway} METRIC {cfg.VpnMetric} IF {ifIndex}", out _, true);
                        RunExe("route", $"ADD 128.0.0.0 MASK 128.0.0.0 {cfg.VpnGateway} METRIC {cfg.VpnMetric} IF {ifIndex}", out _, true);
                        RunExe("netsh", $"interface ip set dnsservers name=\"{ifaceAlias}\" static {cfg.VpnDns} primary", out _, true);
                        ShowBalloon("VPN Toggle", "Restored VPN connection from previous session.");
                    }
                    else
                    {
                        // Restore normal state
                        RestoreNormalState();
                    }
                }
            }
            catch { /* Ignore restoration errors */ }
        }

        private void RestoreNormalState()
        {
            try
            {
                RunExe("route", $"DELETE 0.0.0.0 MASK 128.0.0.0 {cfg.VpnGateway}", out _, true);
                RunExe("route", $"DELETE 128.0.0.0 MASK 128.0.0.0 {cfg.VpnGateway}", out _, true);
                var (ifaceAlias, _) = GetPrimaryInterface();
                RunExe("netsh", $"interface ip set dnsservers name=\"{ifaceAlias}\" static {cfg.NormalDns} primary", out _, true);
                RunExe("ipconfig", "/flushdns", out _, true);
            }
            catch { /* Ignore errors */ }
        }

        // ========= Helpers =========

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
                    var ipv4 = props.UnicastAddresses.FirstOrDefault(a =>
                        a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    var gw4 = props.GatewayAddresses.FirstOrDefault(g =>
                        g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    var idx = props.GetIPv4Properties()?.Index ?? -1;
                    if (ipv4 != null && gw4 != null && idx > 0)
                        return (nic.Name, idx);
                }
                catch { /* keep scanning */ }
            }
            throw new InvalidOperationException("No active IPv4 interface with a gateway was found.");
        }

        /// <summary>Detect our split-route pair via the VPN gateway.</summary>
        private bool IsVpnSplitRoutesPresent()
        {
            var output = RunExe("route", "print -4", out _, true);
            if (string.IsNullOrWhiteSpace(output)) return false;

            bool lo = false, hi = false;
            foreach (var raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var s = Regex.Replace(raw, @"\s+", " ").Trim();
                if (!s.Contains(" " + cfg.VpnGateway + " ")) continue;

                if (s.StartsWith("0.0.0.0 128.0.0.0 ")) lo = true;
                if (s.StartsWith("128.0.0.0 128.0.0.0 ")) hi = true;
                if (lo && hi) return true;
            }
            return false;
        }

        private static bool PingHost(string host)
        {
            try
            {
                using var p = new Ping();
                var reply = p.Send(host, 600);
                return reply?.Status == IPStatus.Success;
            }
            catch { return false; }
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
                Verb = "runas" // requires elevation for route/netsh
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

        private void ShowBalloon(string title, string text, ToolTipIcon icon = ToolTipIcon.Info, int timeoutMs = 2500)
        {
            trayIcon.BalloonTipTitle = title;
            trayIcon.BalloonTipText = text;
            trayIcon.BalloonTipIcon = icon;
            trayIcon.ShowBalloonTip(timeoutMs);
        }

        private static Icon CreateCircleIcon(Color fill)
        {
            // Render a crisp 16x16 circle icon at runtime
            using var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                using var brush = new SolidBrush(fill);
                g.FillEllipse(brush, 1, 1, 14, 14);
                using var pen = new Pen(Color.FromArgb(32, 0, 0, 0));
                g.DrawEllipse(pen, 1, 1, 14, 14);
            }
            // Convert bitmap to Icon
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            using var bmp2 = new Bitmap(ms);
            return Icon.FromHandle(bmp2.GetHicon());
        }

        // ========= Config & Settings =========


        private sealed class SettingsForm : Form
        {
            private readonly Config cfg;
            private readonly TextBox txtVpnGw = new() { Width = 220 };
            private readonly TextBox txtNormalDns = new() { Width = 220 };
            private readonly TextBox txtVpnDns = new() { Width = 220 };
            private readonly NumericUpDown numMetric = new() { Minimum = 1, Maximum = 50, Width = 80, Value = 1 };

            public SettingsForm(Config cfg)
            {
                this.cfg = cfg;
                Text = "VPN Toggle Settings";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = MinimizeBox = false;
                ClientSize = new Size(360, 220);

                var lblVpnGw = new Label { Text = "VPN Gateway:", AutoSize = true, Left = 12, Top = 18 };
                txtVpnGw.Left = 140; txtVpnGw.Top = 14; txtVpnGw.Text = cfg.VpnGateway;

                var lblNormalDns = new Label { Text = "Normal DNS:", AutoSize = true, Left = 12, Top = 56 };
                txtNormalDns.Left = 140; txtNormalDns.Top = 52; txtNormalDns.Text = cfg.NormalDns;

                var lblVpnDns = new Label { Text = "VPN DNS:", AutoSize = true, Left = 12, Top = 94 };
                txtVpnDns.Left = 140; txtVpnDns.Top = 90; txtVpnDns.Text = cfg.VpnDns;

                var lblMetric = new Label { Text = "VPN Metric:", AutoSize = true, Left = 12, Top = 132 };
                numMetric.Left = 140; numMetric.Top = 128; numMetric.Value = cfg.VpnMetric;

                var btnOk = new Button { Text = "Save", DialogResult = DialogResult.OK, Left = 140, Top = 168, Width = 80 };
                var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 236, Top = 168, Width = 80 };

                btnOk.Click += (_, __) =>
                {
                    cfg.VpnGateway = txtVpnGw.Text.Trim();
                    cfg.NormalDns = txtNormalDns.Text.Trim();
                    cfg.VpnDns = txtVpnDns.Text.Trim();
                    cfg.VpnMetric = (int)numMetric.Value;
                    cfg.Save();
                };

                Controls.AddRange(new Control[] {
                    lblVpnGw, txtVpnGw,
                    lblNormalDns, txtNormalDns,
                    lblVpnDns, txtVpnDns,
                    lblMetric, numMetric,
                    btnOk, btnCancel
                });
                AcceptButton = btnOk;
                CancelButton = btnCancel;
            }
        }
    }
}
