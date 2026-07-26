from __future__ import annotations

import voluptuous as vol

from homeassistant.core import HomeAssistant, ServiceCall
from homeassistant.exceptions import HomeAssistantError

from .const import (
	ATTR_APP_INTENT,
	ATTR_DEEP_LINK_URL,
	ATTR_DEVICE_ID,
	ATTR_PACKAGE_NAME,
	ATTR_URL,
	DATA_STORE,
	DOMAIN,
	SERVICE_LOAD_START_URL,
	SERVICE_LOAD_URL,
	SERVICE_START_APPLICATION,
)

LOAD_URL_SCHEMA = vol.Schema(
	{
		vol.Required(ATTR_DEVICE_ID): str,
		vol.Required(ATTR_URL): str,
	}
)

LOAD_START_URL_SCHEMA = vol.Schema(
	{
		vol.Required(ATTR_DEVICE_ID): str,
	}
)

START_APPLICATION_SCHEMA = vol.Schema(
	{
		vol.Required(ATTR_DEVICE_ID): str,
		vol.Optional(ATTR_APP_INTENT): str,
		vol.Optional(ATTR_PACKAGE_NAME): str,
		vol.Optional(ATTR_DEEP_LINK_URL): str,
	}
)


async def async_register_services(hass: HomeAssistant) -> None:
	"""Register services for the Website Kiosk domain."""

	async def _load_url(call: ServiceCall) -> None:
		store = hass.data[DOMAIN][DATA_STORE]
		device_id = _ensure_device(store, call.data[ATTR_DEVICE_ID])

		store.enqueue(
			device_id,
			"load_url",
			url=call.data[ATTR_URL],
		)

	async def _load_start_url(call: ServiceCall) -> None:
		store = hass.data[DOMAIN][DATA_STORE]
		device_id = _ensure_device(store, call.data[ATTR_DEVICE_ID])

		store.enqueue(device_id, "tablet_load_start_url")

	async def _start_application(call: ServiceCall) -> None:
		store = hass.data[DOMAIN][DATA_STORE]
		device_id = _ensure_device(store, call.data[ATTR_DEVICE_ID])

		app_intent = call.data.get(ATTR_APP_INTENT)
		package_name = call.data.get(ATTR_PACKAGE_NAME)
		deep_link_url = call.data.get(ATTR_DEEP_LINK_URL)

		if not app_intent and not package_name and not deep_link_url:
			raise HomeAssistantError(
				"At least one of app_intent, package_name, or deep_link_url is required"
			)

		store.enqueue(
			device_id,
			"start_application",
			appIntent=app_intent,
			packageName=package_name,
			deepLinkUrl=deep_link_url,
		)

	if not hass.services.has_service(DOMAIN, SERVICE_LOAD_URL):
		hass.services.async_register(
			DOMAIN,
			SERVICE_LOAD_URL,
			_load_url,
			schema=LOAD_URL_SCHEMA,
		)

	if not hass.services.has_service(DOMAIN, SERVICE_LOAD_START_URL):
		hass.services.async_register(
			DOMAIN,
			SERVICE_LOAD_START_URL,
			_load_start_url,
			schema=LOAD_START_URL_SCHEMA,
		)

	if not hass.services.has_service(DOMAIN, SERVICE_START_APPLICATION):
		hass.services.async_register(
			DOMAIN,
			SERVICE_START_APPLICATION,
			_start_application,
			schema=START_APPLICATION_SCHEMA,
		)


def _ensure_device(store, device_id: str) -> str:
	normalized = str(device_id).strip()
	if not normalized or not store.has_device(normalized):
		raise HomeAssistantError(f"Unknown kiosk device_id: {device_id}")

	return normalized
