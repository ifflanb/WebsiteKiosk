# Website Kiosk Home Assistant Integration (Starter)

This custom integration exposes command services and entities to control Website Kiosk devices.

## Configuration (`configuration.yaml`)

```yaml
website_kiosk:
  devices:
	- device_id: tablet
	  name: Tablet
	  access_token: YOUR_DEVICE_TOKEN
```

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
