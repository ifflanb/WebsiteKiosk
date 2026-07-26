from __future__ import annotations

import voluptuous as vol

from homeassistant import config_entries
from homeassistant.const import CONF_ACCESS_TOKEN, CONF_NAME

from .const import ATTR_ACCESS_TOKEN, CONF_DEVICE_ID, DEFAULT_DEVICE_NAME, DOMAIN


class WebsiteKioskConfigFlow(config_entries.ConfigFlow, domain=DOMAIN):
	"""Handle a config flow for Website Kiosk."""

	VERSION = 1

	async def async_step_user(self, user_input=None):
		errors: dict[str, str] = {}

		if user_input is not None:
			device_id = str(user_input[CONF_DEVICE_ID]).strip()
			name = str(user_input[CONF_NAME]).strip() or device_id
			access_token = str(user_input.get(CONF_ACCESS_TOKEN, "")).strip() or None

			await self.async_set_unique_id(device_id)
			self._abort_if_unique_id_configured()

			return self.async_create_entry(
				title=name,
				data={
					CONF_DEVICE_ID: device_id,
					CONF_NAME: name,
					ATTR_ACCESS_TOKEN: access_token,
				},
			)

		schema = vol.Schema(
			{
				vol.Required(CONF_DEVICE_ID): str,
				vol.Optional(CONF_NAME, default=DEFAULT_DEVICE_NAME): str,
				vol.Optional(CONF_ACCESS_TOKEN, default=""): str,
			}
		)

		return self.async_show_form(step_id="user", data_schema=schema, errors=errors)

	async def async_step_reconfigure(self, user_input=None):
		"""Handle reconfiguration of an existing entry."""
		entry = self._get_reconfigure_entry()
		errors: dict[str, str] = {}

		if user_input is not None:
			device_id = str(user_input[CONF_DEVICE_ID]).strip()
			name = str(user_input[CONF_NAME]).strip() or device_id
			access_token = str(user_input.get(CONF_ACCESS_TOKEN, "")).strip() or None

			if self._is_device_id_used_by_other_entry(device_id, entry.entry_id):
				errors[CONF_DEVICE_ID] = "already_configured"
			else:
				self.hass.config_entries.async_update_entry(
					entry,
					title=name,
					data={
						CONF_DEVICE_ID: device_id,
						CONF_NAME: name,
						ATTR_ACCESS_TOKEN: access_token,
					},
					unique_id=device_id,
				)
				await self.hass.config_entries.async_reload(entry.entry_id)
				return self.async_abort(reason="reconfigure_successful")

		schema = vol.Schema(
			{
				vol.Required(CONF_DEVICE_ID, default=entry.data.get(CONF_DEVICE_ID, "")): str,
				vol.Optional(CONF_NAME, default=entry.title): str,
				vol.Optional(CONF_ACCESS_TOKEN, default=entry.data.get(ATTR_ACCESS_TOKEN, "") or ""): str,
			}
		)

		return self.async_show_form(step_id="reconfigure", data_schema=schema, errors=errors)

	def _is_device_id_used_by_other_entry(self, device_id: str, current_entry_id: str) -> bool:
		for existing in self.hass.config_entries.async_entries(DOMAIN):
			if existing.entry_id == current_entry_id:
				continue

			existing_device_id = str(existing.data.get(CONF_DEVICE_ID, "")).strip()
			if existing_device_id == device_id:
				return True

		return False
