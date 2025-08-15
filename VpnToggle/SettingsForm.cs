using System;
using System.Drawing;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows.Forms;

namespace VpnToggle
{
    public class SettingsForm : Form
    {
        private TextBox txtIface;
        private TextBox txtVpnGw;
        private TextBox txtNormalDns;
        private TextBox txtVpnDns;
        private NumericUpDown numMetric;
        private Button btnDetect;
        private Button btnSave;
        private Button btnCancel;

        public Config? Result { get; private set; }

        public SettingsForm(Config current)
        {
            Text = "VPN Toggle Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(430, 250);

            var lblIface = new Label { Text = "Interface name (or \"auto\")", Left = 12, Top = 18, Width = 200 };
            txtIface = new TextBox { Left = 220, Top = 15, Width = 180, Text = current.InterfaceName };
            btnDetect = new Button { Left = 350, Top = 45, Width = 50, Text = "Auto" };
            btnDetect.Click += (_, __) => txtIface.Text = DetectPrimaryInterface() ?? "auto";

            var lblVpnGw = new Label { Text = "VPN Gateway (Unraid)", Left = 12, Top = 75, Width = 200 };
            txtVpnGw = new TextBox { Left = 220, Top = 72, Width = 180, Text = current.VpnGateway };

            var lblNormalDns = new Label { Text = "Normal DNS (pfSense)", Left = 12, Top = 105, Width = 200 };
            txtNormalDns = new TextBox { Left = 220, Top = 102, Width = 180, Text = current.NormalDns };

            var lblVpnDns = new Label { Text = "VPN DNS (Mullvad)", Left = 12, Top = 135, Width = 200 };
            txtVpnDns = new TextBox { Left = 220, Top = 132, Width = 180, Text = current.VpnDns };

            var lblMetric = new Label { Text = "VPN Metric", Left = 12, Top = 165, Width = 200 };
            numMetric = new NumericUpDown { Left = 220, Top = 162, Width = 80, Minimum = 1, Maximum = 9999, Value = current.VpnMetric };

            btnSave = new Button { Left = 220, Top = 200, Width = 80, Text = "Save", DialogResult = DialogResult.OK };
            btnCancel = new Button { Left = 320, Top = 200, Width = 80, Text = "Cancel", DialogResult = DialogResult.Cancel };

            btnSave.Click += (_, __) =>
            {
                Result = new Config
                {
                    InterfaceName = txtIface.Text.Trim(),
                    VpnGateway = txtVpnGw.Text.Trim(),
                    NormalDns = txtNormalDns.Text.Trim(),
                    VpnDns = txtVpnDns.Text.Trim(),
                    VpnMetric = (int)numMetric.Value
                };
                Close();
            };

            Controls.AddRange(new Control[]
            {
                lblIface, txtIface, btnDetect,
                lblVpnGw, txtVpnGw,
                lblNormalDns, txtNormalDns,
                lblVpnDns, txtVpnDns,
                lblMetric, numMetric,
                btnSave, btnCancel
            });
        }

        private static string? DetectPrimaryInterface()
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
                    if (ipv4 != null && gw4 != null) return nic.Name;
                }
                catch { }
            }
            return null;
        }
    }
}
