# Website Kiosk Home Assistant Integration

This custom integration exposes command services and entities to control Website Kiosk devices.

## Installation

1. Copy `custom_components/website_kiosk` to:

   `/config/custom_components/website_kiosk`

2. Restart Home Assistant.

## Configuration (UI)

1. Go to **Settings -> Devices & Services**.
2. Click **Add Integration**.
3. Search for **Website Kiosk**.
4. Enter the device settings in the config flow:
   - Device Identifier
   - Display Name
   - Device Access Token (optional)
   - Rotation website URLs (one per line)
   - Rotation Interval
   - Start URL (optional)
   - Screen-Off URL (optional)

## Exposed services

- `website_kiosk.load_url`
- `website_kiosk.load_start_url`
- `website_kiosk.start_application`

## Exposed entities

- `switch.tablet_screen`
- `button.tablet_load_start_url`

## Kiosk polling endpoint

Each device should poll:

`/api/website_kiosk/command/{device_id}`

Authentication options:

- `Authorization: Bearer <access_token>`
- or query string `?access_token=<access_token>`

Response is an object such as:

```json
{
  "id": "...",
  "command": "load_url",
  "url": "https://example.com"
}
```

If no command is pending, response is `{}`.

## Kiosk settings endpoint

Each device can also read configured settings from:

`/api/website_kiosk/settings/{device_id}`
