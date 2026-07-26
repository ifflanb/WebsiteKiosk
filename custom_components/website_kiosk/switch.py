from __future__ import annotations

from homeassistant.components.switch import SwitchEntity
from homeassistant.config_entries import ConfigEntry
from homeassistant.const import CONF_NAME
from homeassistant.core import HomeAssistant
from homeassistant.helpers.entity_platform import AddEntitiesCallback

from .const import DATA_ENTRY_DEVICE, DATA_SCREEN_STATE, DATA_STORE, DOMAIN


async def async_setup_entry(
	hass: HomeAssistant,
	entry: ConfigEntry,
	async_add_entities: AddEntitiesCallback,
) -> None:
	"""Set up Website Kiosk switch from a config entry."""
	device = hass.data[DOMAIN][entry.entry_id][DATA_ENTRY_DEVICE]
	async_add_entities([TabletScreenSwitch(hass, device)])


class TabletScreenSwitch(SwitchEntity):
	"""Expose virtual tablet screen state for kiosk command integration."""

	_attr_has_entity_name = True

	def __init__(self, hass: HomeAssistant, device: dict) -> None:
		self.hass = hass
		self._device_id = device["device_id"]
		self._device_name = device.get(CONF_NAME, self._device_id)
		self._attr_name = "Tablet Screen"
		self._attr_unique_id = f"{DOMAIN}_{self._device_id}_tablet_screen"

	@property
	def is_on(self) -> bool:
		return self.hass.data[DOMAIN][DATA_SCREEN_STATE].get(self._device_id, True)

	@property
	def device_info(self):
		return {
			"identifiers": {(DOMAIN, self._device_id)},
			"name": self._device_name,
			"manufacturer": "WebsiteKiosk",
			"model": "Kiosk Device",
		}

	async def async_turn_on(self, **kwargs) -> None:
		await self._set_state(True)

	async def async_turn_off(self, **kwargs) -> None:
		await self._set_state(False)

	async def _set_state(self, screen_on: bool) -> None:
		self.hass.data[DOMAIN][DATA_SCREEN_STATE][self._device_id] = screen_on

		store = self.hass.data[DOMAIN][DATA_STORE]
		store.enqueue(
			self._device_id,
			"tablet_screen",
			enabled=screen_on,
			value="on" if screen_on else "off",
		)

		self.async_write_ha_state()
