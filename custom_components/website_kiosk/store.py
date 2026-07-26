from __future__ import annotations

from dataclasses import dataclass
from threading import Lock
from typing import Any
from uuid import uuid4


@dataclass(slots=True)
class PendingCommand:
	payload: dict[str, Any]


class CommandStore:
	"""In-memory command queue keyed by kiosk device id."""

	def __init__(self) -> None:
		self._lock = Lock()
		self._device_tokens: dict[str, str | None] = {}
		self._pending_by_device: dict[str, PendingCommand] = {}
		self._settings_by_device: dict[str, dict[str, Any]] = {}

	def set_device_token(self, device_id: str, token: str | None) -> None:
		with self._lock:
			self._device_tokens[device_id] = token

	def register_device(self, device_id: str, token: str | None) -> bool:
		"""Register a device. Returns False if it already exists."""
		with self._lock:
			if device_id in self._device_tokens:
				return False

			self._device_tokens[device_id] = token
			self._settings_by_device.setdefault(device_id, _default_settings())
			return True

	def unregister_device(self, device_id: str) -> None:
		with self._lock:
			self._device_tokens.pop(device_id, None)
			self._pending_by_device.pop(device_id, None)
			self._settings_by_device.pop(device_id, None)

	def is_authorized(self, device_id: str, token: str | None) -> bool:
		with self._lock:
			expected = self._device_tokens.get(device_id)

		if expected is None:
			return True

		return token == expected

	def enqueue(self, device_id: str, command: str, **data: Any) -> str:
		command_id = str(uuid4())
		payload: dict[str, Any] = {
			"id": command_id,
			"command": command,
		}
		payload.update({key: value for key, value in data.items() if value is not None})

		with self._lock:
			self._pending_by_device[device_id] = PendingCommand(payload=payload)

		return command_id

	def dequeue(self, device_id: str) -> dict[str, Any] | None:
		with self._lock:
			pending = self._pending_by_device.pop(device_id, None)

		return pending.payload if pending else None

	def has_device(self, device_id: str) -> bool:
		with self._lock:
			return device_id in self._device_tokens

	def device_ids(self) -> list[str]:
		with self._lock:
			return list(self._device_tokens.keys())

	def ensure_device(self, device_id: str) -> None:
		with self._lock:
			self._device_tokens.setdefault(device_id, None)
			self._settings_by_device.setdefault(device_id, _default_settings())

	def set_settings(self, device_id: str, settings: dict[str, Any]) -> dict[str, Any]:
		"""Replace settings for a device with normalized values."""
		normalized = _normalize_settings(settings)
		with self._lock:
			if device_id not in self._device_tokens:
				raise KeyError(device_id)

			self._settings_by_device[device_id] = normalized

		return normalized.copy()

	def get_settings(self, device_id: str) -> dict[str, Any] | None:
		with self._lock:
			settings = self._settings_by_device.get(device_id)

		if settings is None:
			return None

		return settings.copy()


def _default_settings() -> dict[str, Any]:
	return {
		"websites": [],
		"rotate_frequency_seconds": 30,
		"start_url": None,
		"screen_off_url": None,
	}


def _normalize_settings(settings: dict[str, Any]) -> dict[str, Any]:
	websites_input = settings.get("websites")
	websites: list[dict[str, Any]] = []
	if isinstance(websites_input, list):
		for index, item in enumerate(websites_input):
			if not isinstance(item, dict):
				continue

			url = item.get("url")
			if not isinstance(url, str) or not url.strip():
				continue

			order = item.get("order")
			try:
				order_value = int(order) if order is not None else index + 1
			except (TypeError, ValueError):
				order_value = index + 1

			websites.append(
				{
					"url": url.strip(),
					"order": order_value if order_value > 0 else index + 1,
				}
			)

	rotate_input = settings.get("rotate_frequency_seconds")
	try:
		rotate_frequency_seconds = int(rotate_input)
	except (TypeError, ValueError):
		rotate_frequency_seconds = 30

	if rotate_frequency_seconds <= 0:
		rotate_frequency_seconds = 30

	start_url = settings.get("start_url")
	if isinstance(start_url, str):
		start_url = start_url.strip() or None
	else:
		start_url = None

	sorted_websites = sorted(websites, key=lambda x: int(x.get("order", 0)))

	if start_url is None and sorted_websites:
		first_url = sorted_websites[0].get("url")
		if isinstance(first_url, str) and first_url.strip():
			start_url = first_url.strip()

	screen_off_url = settings.get("screen_off_url")
	if isinstance(screen_off_url, str):
		screen_off_url = screen_off_url.strip() or None
	else:
		screen_off_url = None

	return {
		"websites": sorted_websites,
		"rotate_frequency_seconds": rotate_frequency_seconds,
		"start_url": start_url,
		"screen_off_url": screen_off_url,
	}
