from __future__ import annotations

import voluptuous as vol

from homeassistant import config_entries
from homeassistant.const import CONF_ACCESS_TOKEN, CONF_NAME
from homeassistant.helpers.selector import NumberSelector, NumberSelectorConfig, NumberSelectorMode, TextSelector, TextSelectorConfig

from .const import (
	ATTR_ACCESS_TOKEN,
	ATTR_ROTATE_FREQUENCY_SECONDS,
	ATTR_SCREEN_OFF_URL,
	ATTR_START_URL,
	ATTR_WEBSITES,
	CONF_DEVICE_ID,
	DEFAULT_DEVICE_NAME,
	DOMAIN,
)


class WebsiteKioskConfigFlow(config_entries.ConfigFlow, domain=DOMAIN):
	"""Handle a config flow for Website Kiosk."""

	VERSION = 1

	async def async_step_user(self, user_input=None):
		errors: dict[str, str] = {}

		if user_input is not None:
			device_id = str(user_input[CONF_DEVICE_ID]).strip()
			name = str(user_input[CONF_NAME]).strip() or device_id
			access_token = str(user_input.get(CONF_ACCESS_TOKEN, "")).strip() or None
			websites = _parse_websites(str(user_input.get(ATTR_WEBSITES, "") or ""))
			rotate_frequency_seconds = _parse_rotate_frequency(
				user_input.get(ATTR_ROTATE_FREQUENCY_SECONDS)
			)
			start_url = _clean_optional_text(user_input.get(ATTR_START_URL))
			screen_off_url = _clean_optional_text(user_input.get(ATTR_SCREEN_OFF_URL))

			await self.async_set_unique_id(device_id)
			self._abort_if_unique_id_configured()

			return self.async_create_entry(
				title=name,
				data={
					CONF_DEVICE_ID: device_id,
					CONF_NAME: name,
					ATTR_ACCESS_TOKEN: access_token,
					ATTR_WEBSITES: websites,
					ATTR_ROTATE_FREQUENCY_SECONDS: rotate_frequency_seconds,
					ATTR_START_URL: start_url,
					ATTR_SCREEN_OFF_URL: screen_off_url,
				},
			)

		schema = vol.Schema(
			{
				vol.Required(CONF_DEVICE_ID): TextSelector(),
				vol.Optional(CONF_NAME, default=DEFAULT_DEVICE_NAME): TextSelector(),
				vol.Optional(CONF_ACCESS_TOKEN, default=""): TextSelector(),
				vol.Optional(ATTR_WEBSITES, default=""): TextSelector(
					TextSelectorConfig(multiline=True)
				),
				vol.Optional(ATTR_ROTATE_FREQUENCY_SECONDS, default=30): NumberSelector(
					NumberSelectorConfig(min=1, mode=NumberSelectorMode.BOX)
				),
				vol.Optional(ATTR_START_URL, default=""): TextSelector(),
				vol.Optional(ATTR_SCREEN_OFF_URL, default=""): TextSelector(),
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
			websites = _parse_websites(str(user_input.get(ATTR_WEBSITES, "") or ""))
			rotate_frequency_seconds = _parse_rotate_frequency(
				user_input.get(ATTR_ROTATE_FREQUENCY_SECONDS)
			)
			start_url = _clean_optional_text(user_input.get(ATTR_START_URL))
			screen_off_url = _clean_optional_text(user_input.get(ATTR_SCREEN_OFF_URL))

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
						ATTR_WEBSITES: websites,
						ATTR_ROTATE_FREQUENCY_SECONDS: rotate_frequency_seconds,
						ATTR_START_URL: start_url,
						ATTR_SCREEN_OFF_URL: screen_off_url,
					},
					unique_id=device_id,
				)
				await self.hass.config_entries.async_reload(entry.entry_id)
				return self.async_abort(reason="reconfigure_successful")

		schema = vol.Schema(
			{
				vol.Required(CONF_DEVICE_ID, default=entry.data.get(CONF_DEVICE_ID, "")): TextSelector(),
				vol.Optional(CONF_NAME, default=entry.title): TextSelector(),
				vol.Optional(CONF_ACCESS_TOKEN, default=entry.data.get(ATTR_ACCESS_TOKEN, "") or ""): TextSelector(),
				vol.Optional(
					ATTR_WEBSITES,
					default=_websites_to_multiline(entry.data.get(ATTR_WEBSITES, [])),
				): TextSelector(TextSelectorConfig(multiline=True)),
				vol.Optional(
					ATTR_ROTATE_FREQUENCY_SECONDS,
					default=_parse_rotate_frequency(
						entry.data.get(ATTR_ROTATE_FREQUENCY_SECONDS, 30)
					),
				): NumberSelector(NumberSelectorConfig(min=1, mode=NumberSelectorMode.BOX)),
				vol.Optional(ATTR_START_URL, default=entry.data.get(ATTR_START_URL, "") or ""): TextSelector(),
				vol.Optional(
					ATTR_SCREEN_OFF_URL,
					default=entry.data.get(ATTR_SCREEN_OFF_URL, "") or "",
				): TextSelector(),
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


def _parse_websites(value: str) -> list[dict[str, int | str]]:
	if "\n" not in value and "\r" not in value:
		value = _split_comma_separated_urls(value)

	websites: list[dict[str, int | str]] = []
	for index, line in enumerate(value.splitlines()):
		url = line.strip()
		if not url:
			continue

		websites.append({"url": url, "order": index + 1})

	return websites


def _split_comma_separated_urls(value: str) -> str:
	parts = [part.strip() for part in value.split(",")]
	if len(parts) <= 1:
		return value

	if all(part.lower().startswith(("http://", "https://")) for part in parts if part):
		return "\n".join(part for part in parts if part)

	return value


def _websites_to_multiline(websites: object) -> str:
	if not isinstance(websites, list):
		return ""

	lines: list[str] = []
	for item in websites:
		if not isinstance(item, dict):
			continue

		url = item.get("url")
		if isinstance(url, str) and url.strip():
			lines.append(url.strip())

	return "\n".join(lines)


def _parse_rotate_frequency(value: object) -> int:
	try:
		parsed = int(value)
	except (TypeError, ValueError):
		return 30

	return parsed if parsed > 0 else 30


def _clean_optional_text(value: object) -> str | None:
	if not isinstance(value, str):
		return None

	cleaned = value.strip()
	return cleaned or None
