# WebsiteKiosk

Blazor WebAssembly kiosk app for rotating website dashboards, with a Home Assistant custom integration for remote control.

## Prerequisites

- .NET 10 SDK
- Home Assistant (for integration features)
- `dotnet-serve` tool for static hosting

Install `dotnet-serve` if needed:

```powershell
dotnet tool install -g dotnet-serve
```

## Build and publish the kiosk app

From the repository root:

```powershell
dotnet publish -c Release -o C:\apps\WebsiteKiosk\publish
```

## Serve the published app (LAN-accessible)

```powershell
& "$env:USERPROFILE\.dotnet\tools\dotnet-serve.exe" -d C:\apps\WebsiteKiosk\publish\wwwroot -p 8080 -a 0.0.0.0
```

Open from another device:

```text
http://<host-machine-ip>:8080
```

## Install the Home Assistant integration

Copy this folder into your HA config directory:

```text
custom_components/website_kiosk
```

Destination on HA host:

```text
/config/custom_components/website_kiosk
```

Restart Home Assistant Core.

In Home Assistant UI:

1. Go to **Settings -> Devices & Services**
2. Click **Add Integration**
3. Search for **Website Kiosk**
4. Add a device entry (example `device_id`: `ipad_study`)

## Configure kiosk app integration settings

Open kiosk admin page:

```text
http://<host-machine-ip>:8080/admin
```

Set:

- **Command URL**: `http://<ha-host>:8123/api/website_kiosk/command/<device_id>`
  - Example: `http://192.168.68.117:8123/api/website_kiosk/command/ipad_study`
- **Access token**: leave empty unless you explicitly configured one in HA
- **Poll frequency (secs)**: e.g. `2` to `5`

When Command URL is configured, runtime settings are HA-managed. The app automatically reads settings from:

`http://<ha-host>:8123/api/website_kiosk/settings/<device_id>`

## Home Assistant CORS configuration (required for browser-based kiosk polling)

In `configuration.yaml`, allow the origin serving the kiosk app (not the iPad IP):

```yaml
http:
  cors_allowed_origins:
	- http://<kiosk-host-ip>:8080
	- http://<kiosk-host-ip>
```

Example:

```yaml
http:
  cors_allowed_origins:
	- http://192.168.68.108:8080
	- http://192.168.68.108
```

Restart Home Assistant after changing CORS settings.

## Quick test

1. In Home Assistant, call service `website_kiosk.set_settings` with data like:

```yaml
device_id: ipad_study
rotate_frequency_seconds: 20
start_url: https://sharptools.io/dashboard/view/abc
screen_off_url: https://example.com/screen-off
websites:
  - url: https://sharptools.io/dashboard/view/abc
	order: 1
  - url: https://example.com
	order: 2
```

2. Start slideshow on the kiosk app.
3. In Home Assistant, press `button.tablet_load_start_url`.
4. Kiosk should navigate to HA-configured Start URL (or first HA website URL).

## Notes

- `start_application` actions are Android-only by design.
- Integration tile branding in Home Assistant may require Brands pipeline support for full icon rendering in all HA views.
