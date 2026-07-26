from __future__ import annotations

import logging

from homeassistant.config_entries import ConfigEntry
from homeassistant.core import HomeAssistant

from .const import (
	ATTR_ACCESS_TOKEN,
	CONF_DEVICE_ID,
	DATA_ENTRY_DEVICE,
	DATA_HTTP_VIEW_REGISTERED,
	DATA_SCREEN_STATE,
	DATA_SERVICES_REGISTERED,
	DATA_STORE,
	DOMAIN,
	PLATFORMS,
)
from .http_api import WebsiteKioskCommandView, WebsiteKioskSettingsView
from .services import async_register_services
from .store import CommandStore

_LOGGER = logging.getLogger(__name__)


def _ensure_domain_data(hass: HomeAssistant) -> dict:
	hass.data.setdefault(DOMAIN, {})
	domain_data = hass.data[DOMAIN]

	if DATA_STORE not in domain_data:
		domain_data[DATA_STORE] = CommandStore()

	domain_data.setdefault(DATA_SCREEN_STATE, {})

	return domain_data


async def async_setup(hass: HomeAssistant, config: dict) -> bool:
	"""Set up integration domain container."""
	domain_data = _ensure_domain_data(hass)

	if not domain_data.get(DATA_HTTP_VIEW_REGISTERED):
		hass.http.register_view(WebsiteKioskCommandView(hass))
		hass.http.register_view(WebsiteKioskSettingsView(hass))
		domain_data[DATA_HTTP_VIEW_REGISTERED] = True

	if not domain_data.get(DATA_SERVICES_REGISTERED):
		await async_register_services(hass)
		domain_data[DATA_SERVICES_REGISTERED] = True

	return True


async def async_setup_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
	"""Set up a Website Kiosk device from a config entry."""
	domain_data = _ensure_domain_data(hass)
	store: CommandStore = domain_data[DATA_STORE]

	device_id = str(entry.data.get(CONF_DEVICE_ID, "")).strip()
	if not device_id:
		_LOGGER.error("Config entry %s missing device_id", entry.entry_id)
		return False

	access_token = entry.data.get(ATTR_ACCESS_TOKEN)
	if isinstance(access_token, str):
		access_token = access_token.strip() or None
	else:
		access_token = None

	if not store.register_device(device_id, access_token):
		if entry.entry_id not in domain_data:
			_LOGGER.error("Duplicate device_id '%s' already configured", device_id)
			return False

	domain_data.setdefault(DATA_SCREEN_STATE, {})
	domain_data[DATA_SCREEN_STATE].setdefault(device_id, True)
	domain_data[entry.entry_id] = {
		DATA_ENTRY_DEVICE: {
			"device_id": device_id,
			"name": entry.title,
			ATTR_ACCESS_TOKEN: access_token,
		}
	}

	await hass.config_entries.async_forward_entry_setups(entry, PLATFORMS)
	return True


async def async_unload_entry(hass: HomeAssistant, entry: ConfigEntry) -> bool:
	"""Unload a Website Kiosk config entry."""
	unloaded = await hass.config_entries.async_unload_platforms(entry, PLATFORMS)
	if not unloaded:
		return False

	domain_data = hass.data.get(DOMAIN, {})
	entry_data = domain_data.pop(entry.entry_id, None)
	if not entry_data:
		return True

	device = entry_data.get(DATA_ENTRY_DEVICE, {})
	device_id = device.get("device_id")
	if not device_id:
		return True

	store: CommandStore = domain_data.get(DATA_STORE)
	if store:
		store.unregister_device(device_id)

	screen_state = domain_data.get(DATA_SCREEN_STATE)
	if isinstance(screen_state, dict):
		screen_state.pop(device_id, None)

	return True
