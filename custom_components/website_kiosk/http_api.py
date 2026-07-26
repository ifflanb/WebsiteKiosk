from __future__ import annotations

from homeassistant.components.http import HomeAssistantView
from homeassistant.core import HomeAssistant

from .const import API_COMMAND_PATH, API_SETTINGS_PATH, DATA_STORE, DOMAIN


class WebsiteKioskCommandView(HomeAssistantView):
	"""Return pending command payloads for kiosk devices."""

	url = f"{API_COMMAND_PATH}/{{device_id}}"
	name = "api:website_kiosk:command"
	requires_auth = False

	def __init__(self, hass: HomeAssistant) -> None:
		self.hass = hass

	async def get(self, request, device_id: str):
		integration_data = self.hass.data.get(DOMAIN)
		if not integration_data:
			return self.json_message("Integration not initialized", status_code=503)

		store = integration_data[DATA_STORE]
		if not store.has_device(device_id):
			return self.json_message("Unknown device", status_code=404)

		token = _extract_token(request)
		if not store.is_authorized(device_id, token):
			return self.json_message("Unauthorized", status_code=401)

		payload = store.dequeue(device_id) or {}
		return self.json(payload)


class WebsiteKioskSettingsView(HomeAssistantView):
	"""Return current settings payload for kiosk devices."""

	url = f"{API_SETTINGS_PATH}/{{device_id}}"
	name = "api:website_kiosk:settings"
	requires_auth = False

	def __init__(self, hass: HomeAssistant) -> None:
		self.hass = hass

	async def get(self, request, device_id: str):
		integration_data = self.hass.data.get(DOMAIN)
		if not integration_data:
			return self.json_message("Integration not initialized", status_code=503)

		store = integration_data[DATA_STORE]
		if not store.has_device(device_id):
			return self.json_message("Unknown device", status_code=404)

		token = _extract_token(request)
		if not store.is_authorized(device_id, token):
			return self.json_message("Unauthorized", status_code=401)

		payload = store.get_settings(device_id) or {}
		return self.json(payload)

def _extract_token(request) -> str | None:
	auth_header = request.headers.get("Authorization", "")
	if auth_header.lower().startswith("bearer "):
		return auth_header[7:].strip() or None

	query_token = request.query.get("access_token")
	if isinstance(query_token, str):
		return query_token.strip() or None

	return None
