from __future__ import annotations

from homeassistant.components.button import ButtonEntity
from homeassistant.config_entries import ConfigEntry
from homeassistant.const import CONF_NAME
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity_platform import AddEntitiesCallback

from .const import DATA_ENTRY_DEVICE, DATA_STORE, DOMAIN


async def async_setup_entry(
	hass: HomeAssistant,
	entry: ConfigEntry,
	async_add_entities: AddEntitiesCallback,
) -> None:
	"""Set up Website Kiosk button from a config entry."""
	device = hass.data[DOMAIN][entry.entry_id][DATA_ENTRY_DEVICE]
	async_add_entities([TabletLoadStartUrlButton(hass, device)])


class TabletLoadStartUrlButton(ButtonEntity):
	"""Trigger load_start_url behavior on kiosk devices."""

	_attr_has_entity_name = True

	def __init__(self, hass: HomeAssistant, device: dict) -> None:
		self.hass = hass
		self._device_id = device["device_id"]
		self._device_name = device.get(CONF_NAME, self._device_id)
		self._attr_name = "Tablet Load Start URL"
		self._attr_unique_id = f"{DOMAIN}_{self._device_id}_tablet_load_start_url"

	@property
	def device_info(self):
		return {
			"identifiers": {(DOMAIN, self._device_id)},
			"name": self._device_name,
			"manufacturer": "WebsiteKiosk",
			"model": "Kiosk Device",
		}

	async def async_press(self) -> None:
		store = self.hass.data[DOMAIN][DATA_STORE]
		store.enqueue(self._device_id, "tablet_load_start_url")
