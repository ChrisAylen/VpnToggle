# VpnToggle

A lightweight Windows system tray application for quickly toggling between VPN and normal internet routing using split-tunnel techniques. Perfect for scenarios where you need to selectively route traffic through a VPN gateway while maintaining local network access.

## Features

- **One-click VPN toggle** from system tray
- **Split-tunnel routing** using Windows route table manipulation
- **Automatic DNS switching** between normal and VPN DNS servers
- **Visual status indicators** with colored tray icons
- **Network interface auto-detection**
- **Configurable settings** via GUI
- **Runs elevated** for route table access
- **Minimal resource usage** - stays in system tray

## How It Works

VpnToggle uses Windows routing table manipulation to create a "split-tunnel" effect:

- **Normal Mode**: Traffic routes through your default gateway (e.g., pfSense router)
- **VPN Mode**: Adds two high-priority routes (`0.0.0.0/1` and `128.0.0.0/1`) that collectively cover all internet traffic, directing it through your VPN gateway

This approach allows you to:
- Keep local network access while using VPN
- Quickly switch between VPN and normal routing
- Avoid full VPN client overhead

## Prerequisites

- Windows 10/11
- Administrative privileges (required for route table modifications)
- A VPN gateway accessible on your local network
- .NET 8 Runtime (if using framework-dependent build)

## Installation

### Option 1: Installer (Recommended)
1. Download `VpnToggle-Setup.exe` from the [latest release](../../releases/latest)
2. Run the installer as administrator
3. Choose to run at startup (creates elevated scheduled task)
4. Launch immediately or restart Windows

### Option 2: Portable
1. Download `VpnToggle-{version}-win-x64.zip` (Intel/AMD 64-bit)
2. Extract to desired location
3. Run `VpnToggle.exe` as administrator

## Configuration

On first run, VpnToggle creates a configuration file at:
```
%AppData%\VpnToggle\config.json
```

This file stores both your network settings and the last known VPN state (on/off) so the application can restore your preferred state when Windows restarts.

### Default Settings
- **VPN Gateway**: `10.0.0.9` (Unraid WireGuard NAT IP)
- **Normal DNS**: `10.0.0.1` (pfSense)
- **VPN DNS**: `10.64.0.1` (Mullvad DNS)
- **VPN Metric**: `1` (route priority)

### Customizing Settings
1. Right-click the tray icon
2. Select "Settings..."
3. Modify values as needed:
   - **VPN Gateway**: IP address of your VPN endpoint
   - **Normal DNS**: Your regular DNS server
   - **VPN DNS**: DNS server to use when VPN is active
   - **VPN Metric**: Route priority (lower = higher priority)
4. Click "Save"

## Usage

### Basic Operations
- **Double-click tray icon**: Toggle VPN on/off
- **Right-click tray icon**: Access context menu
- **Green icon**: VPN active
- **Red icon**: Normal routing

### Context Menu Options
- **Toggle VPN**: Switch between VPN and normal routing
- **Show Status**: Display current interface and routing mode
- **Settings...**: Open configuration dialog
- **Exit**: Close application

### Safety Features
- **Gateway reachability check**: Won't switch to VPN if gateway is unreachable
- **Automatic cleanup**: Removes conflicting routes before applying new ones
- **DNS flush**: Clears DNS cache when switching modes
- **State persistence**: Remembers VPN on/off state across reboots and restores it automatically
- **Startup restoration**: Detects and fixes connectivity issues from previous sessions

## Network Architecture Example

This tool was designed for a specific network setup but can be adapted:

```
Internet
   ↓
pfSense Router (10.0.0.1)
   ↓
Local Network (10.0.0.0/24)
   ├── Windows PC (running VpnToggle)
   └── Unraid Server (10.0.0.9)
       └── WireGuard VPN (to Mullvad)
```

## Building from Source

### Prerequisites
- .NET 8 SDK
- Windows 10/11
- Visual Studio 2022 or VS Code (optional)

### Build Commands
```bash
# Restore dependencies
dotnet restore

# Build (Debug)
dotnet build

# Build (Release)
dotnet build -c Release

# Publish self-contained executable
dotnet publish VpnToggle/VpnToggle.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# Note: Installer builds use the version from git tags (/DMyVersion) via GitHub Actions

# Run tests (if any exist)
dotnet test
```

### GitHub Actions
The project includes automated CI/CD workflows:
- **CI**: Builds and tests on every push/PR
- **Release**: Creates releases with installers when tags are pushed

To create a release:
```bash
git tag v1.0.0
git push origin v1.0.0
```

**Note**: You must use a new tag version for each release (e.g., v1.0.1, v1.0.2) as GitHub Actions only triggers on new tags.

## Troubleshooting

### Common Issues

**"Access Denied" errors**
- Ensure you're running as administrator
- Check Windows UAC settings

**VPN gateway not reachable**
- Verify the gateway IP in settings
- Check network connectivity to VPN server
- Ensure firewall isn't blocking traffic

**Routes not applying**
- Check if other VPN software is interfering
- Verify interface name in settings
- Run `route print` to check existing routes

**DNS not switching**
- Verify DNS server IPs in settings
- Check Windows network adapter settings
- Try running `ipconfig /flushdns` manually

**No internet after reboot/startup**
- The app automatically detects and fixes this issue
- If problems persist, try toggling VPN off then on
- Check that your VPN gateway is reachable from your network

**VPN state not restored after reboot**
- Check that the app is running with elevated privileges
- Verify the configuration file exists at `%AppData%\VpnToggle\config.json`
- Look for startup restoration messages in system tray notifications

### Manual Route Cleanup
If routes get stuck, manually remove them:
```cmd
route DELETE 0.0.0.0 MASK 128.0.0.0 [VPN_GATEWAY_IP]
route DELETE 128.0.0.0 MASK 128.0.0.0 [VPN_GATEWAY_IP]
```

### Logs and Debugging
VpnToggle shows status information via system tray balloon notifications. For detailed troubleshooting:
1. Right-click tray icon → "Show Status"
2. Check Windows Event Viewer for errors
3. Verify route table with `route print -4`

## Technical Details

### Route Table Manipulation
- Creates two `/1` routes instead of a single `/0` route
- Higher priority than default routes (lower metric)
- Preserves local network access

### Elevation Requirements
- Uses `Verb = "runas"` for route/netsh commands
- Scheduled task runs with highest privileges
- Required for Windows route table modifications

### Network Interface Detection
- Automatically finds primary IPv4 interface
- Looks for interfaces with active gateway
- Handles multiple network adapters gracefully

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add/update tests if applicable
5. Submit a pull request

## License

This project is licensed under the MIT License - see the [LICENSE.txt](LICENSE.txt) file for details.

## Security Considerations

- **Runs with elevated privileges**: Required for route manipulation
- **Network traffic redirection**: Understand routing implications
- **DNS changes**: Be aware of DNS leak potential
- **Local network access**: Maintains access to local resources

## Acknowledgments

- Built with .NET 8 and Windows Forms
- Uses Windows routing table and netsh for network configuration
- Designed for Unraid + WireGuard + Mullvad setup but adaptable to other configurations