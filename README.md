# Website Kiosk

![Website Kiosk Logo](wwwroot/images/logo.png)

Website Kiosk is a Blazor WebAssembly kiosk app plus a Home Assistant custom integration.

- Home Assistant stores kiosk device settings (rotation URLs, interval, start URL, screen-off URL).
- The kiosk web app polls Home Assistant for settings and commands.

[![Add Integration to My Home Assistant](https://my.home-assistant.io/badges/config_flow_start.svg)](https://my.home-assistant.io/redirect/config_flow_start?domain=website_kiosk)

## Install in HACS (recommended)

1. Open **HACS** in Home Assistant.
2. Go to **Integrations**.
3. Click the menu (⋮) -> **Custom repositories**.
4. Add this repository URL:

   `https://github.com/ifflanb/WebsiteKiosk`

5. Category: **Integration**.
6. Click **Add**.
7. Find **Website Kiosk** in HACS and click **Download**.
8. Restart Home Assistant.

Then configure it from:

**Settings -> Devices & Services -> Add Integration -> Website Kiosk**

## Manual integration install (alternative)

Copy this folder to your HA config:

`custom_components/website_kiosk` -> `/config/custom_components/website_kiosk`

Restart Home Assistant and add the integration from **Settings -> Devices & Services**.

## Host the kiosk website

The integration only provides commands/settings endpoints. You still need to host the kiosk web app and open it on the kiosk device.

1. Open the latest release page:

   `https://github.com/ifflanb/WebsiteKiosk/releases/latest`

2. In **Assets**, download the website package zip (for example: `WebsiteKiosk-web-v0.1.2.zip`).

3. Create a folder on your host machine, for example:

   `C:\apps\WebsiteKiosk\site`

4. Extract the zip contents into that folder.

5. Host the extracted files with a static web server.

   Example with `dotnet-serve`:

   ```powershell
   & "$env:USERPROFILE\.dotnet\tools\dotnet-serve.exe" -d C:\apps\WebsiteKiosk\site -p 8080 -a 0.0.0.0
   ```

6. Open from kiosk device:

   `http://<kiosk-host-ip>:8080/`

If your release zip contains a `wwwroot` folder at the top level, point the server to that folder instead.

### Build from source (optional)

Only needed if you want to build your own website package.

Prereqs:

- `dotnet-serve` (`dotnet tool install -g dotnet-serve`)

Build/publish:

- .NET 10 SDK

Publish from source:

```powershell
dotnet publish -c Release -o C:\apps\WebsiteKiosk\publish
```

Serve:

```powershell
& "$env:USERPROFILE\.dotnet\tools\dotnet-serve.exe" -d C:\apps\WebsiteKiosk\publish\wwwroot -p 8080 -a 0.0.0.0
```

Open from kiosk device:

`http://<kiosk-host-ip>:8080/`

## Connect kiosk app to Home Assistant

In the website settings page (`/`):

- Enter **Home Assistant base URL**
- Enter **Device identifier** (must match the integration entry)
- Optional: **Access token**
- Click **Save settings**

The app uses:

- Command endpoint: `/api/website_kiosk/command/{device_id}`
- Settings endpoint: `/api/website_kiosk/settings/{device_id}`

## Required CORS setting in Home Assistant

In `configuration.yaml`, allow the kiosk website origin:

```yaml
http:
  cors_allowed_origins:
	- http://<kiosk-host-ip>:8080
	- http://<kiosk-host-ip>
```

Restart Home Assistant after updating CORS.

## Notes

- `start_application` actions are Android-only.
- If kiosk host IP changes, update HA CORS allowed origins.
