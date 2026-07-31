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
		self._device_lookup: dict[str, str] = {}
		self._pending_by_device: dict[str, PendingCommand] = {}
		self._settings_by_device: dict[str, dict[str, Any]] = {}

	def set_device_token(self, device_id: str, token: str | None) -> None:
		normalized = _normalize_device_id(device_id)
		if not normalized:
			return

		with self._lock:
			resolved = self._resolve_device_id_locked(normalized)
			if resolved is None:
				self._device_tokens[normalized] = token
				self._settings_by_device.setdefault(normalized, _default_settings())
				self._device_lookup[_device_lookup_key(normalized)] = normalized
				return

			self._device_tokens[resolved] = token

	def register_device(self, device_id: str, token: str | None) -> bool:
		"""Register a device. Returns False if it already exists."""
		normalized = _normalize_device_id(device_id)
		if not normalized:
			return False

		with self._lock:
			if self._resolve_device_id_locked(normalized) is not None:
				return False

			self._device_tokens[normalized] = token
			self._settings_by_device.setdefault(normalized, _default_settings())
			self._device_lookup[_device_lookup_key(normalized)] = normalized
			return True

	def unregister_device(self, device_id: str) -> None:
		normalized = _normalize_device_id(device_id)
		if not normalized:
			return

		with self._lock:
			resolved = self._resolve_device_id_locked(normalized)
			if resolved is None:
				return

			self._device_tokens.pop(resolved, None)
			self._pending_by_device.pop(resolved, None)
			self._settings_by_device.pop(resolved, None)
			self._device_lookup.pop(_device_lookup_key(resolved), None)

	def is_authorized(self, device_id: str, token: str | None) -> bool:
		normalized = _normalize_device_id(device_id)
		if not normalized:
			return False

		with self._lock:
			resolved = self._resolve_device_id_locked(normalized)
			if resolved is None:
				return False

			expected = self._device_tokens.get(resolved)

		if expected is None:
			return True

		return token == expected

	def enqueue(self, device_id: str, command: str, **data: Any) -> str:
		normalized = _normalize_device_id(device_id)
		if not normalized:
			raise KeyError(device_id)

		command_id = str(uuid4())
		payload: dict[str, Any] = {
			"id": command_id,
			"command": command,
		}
		payload.update({key: value for key, value in data.items() if value is not None})

		with self._lock:
			resolved = self._resolve_device_id_locked(normalized)
			if resolved is None:
				raise KeyError(device_id)

			self._pending_by_device[resolved] = PendingCommand(payload=payload)

		return command_id

	def dequeue(self, device_id: str) -> dict[str, Any] | None:
		normalized = _normalize_device_id(device_id)
		if not normalized:
			return None

		with self._lock:
			resolved = self._resolve_device_id_locked(normalized)
			if resolved is None:
				return None

			pending = self._pending_by_device.pop(resolved, None)

		return pending.payload if pending else None

	def has_device(self, device_id: str) -> bool:
		normalized = _normalize_device_id(device_id)
		if not normalized:
			return False

		with self._lock:
			return self._resolve_device_id_locked(normalized) is not None

	def device_ids(self) -> list[str]:
		with self._lock:
			return list(self._device_tokens.keys())

	def ensure_device(self, device_id: str) -> None:
		normalized = _normalize_device_id(device_id)
		if not normalized:
			return

		with self._lock:
			resolved = self._resolve_device_id_locked(normalized)
			if resolved is not None:
				self._device_tokens.setdefault(resolved, None)
				self._settings_by_device.setdefault(resolved, _default_settings())
				return

			self._device_tokens.setdefault(normalized, None)
			self._settings_by_device.setdefault(normalized, _default_settings())
			self._device_lookup[_device_lookup_key(normalized)] = normalized

	def set_settings(self, device_id: str, settings: dict[str, Any]) -> dict[str, Any]:
		"""Replace settings for a device with normalized values."""
		normalized_device_id = _normalize_device_id(device_id)
		if not normalized_device_id:
			raise KeyError(device_id)

		normalized = _normalize_settings(settings)
		with self._lock:
			resolved = self._resolve_device_id_locked(normalized_device_id)
			if resolved is None:
				raise KeyError(device_id)

			self._settings_by_device[resolved] = normalized

		return normalized.copy()

	def get_settings(self, device_id: str) -> dict[str, Any] | None:
		normalized = _normalize_device_id(device_id)
		if not normalized:
			return None

		with self._lock:
			resolved = self._resolve_device_id_locked(normalized)
			if resolved is None:
				return None

			settings = self._settings_by_device.get(resolved)

		if settings is None:
			return None

		return settings.copy()

	def _resolve_device_id_locked(self, device_id: str) -> str | None:
		lookup_key = _device_lookup_key(device_id)
		resolved = self._device_lookup.get(lookup_key)
		if resolved is None:
			return None

		if resolved not in self._device_tokens:
			self._device_lookup.pop(lookup_key, None)
			return None

		return resolved


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


def _normalize_device_id(value: object) -> str:
	return str(value).strip()


def _device_lookup_key(device_id: str) -> str:
	return device_id.casefold()
