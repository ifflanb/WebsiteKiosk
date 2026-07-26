DOMAIN = "website_kiosk"

PLATFORMS = ["switch", "button"]

CONF_DEVICE_ID = "device_id"
DEFAULT_DEVICE_NAME = "Kiosk Device"

SERVICE_LOAD_URL = "load_url"
SERVICE_START_APPLICATION = "start_application"
SERVICE_LOAD_START_URL = "load_start_url"
SERVICE_SET_SETTINGS = "set_settings"

ATTR_DEVICE_ID = "device_id"
ATTR_URL = "url"
ATTR_APP_INTENT = "app_intent"
ATTR_PACKAGE_NAME = "package_name"
ATTR_DEEP_LINK_URL = "deep_link_url"
ATTR_SCREEN_ON = "screen_on"
ATTR_ACCESS_TOKEN = "access_token"
ATTR_WEBSITES = "websites"
ATTR_ROTATE_FREQUENCY_SECONDS = "rotate_frequency_seconds"
ATTR_START_URL = "start_url"
ATTR_SCREEN_OFF_URL = "screen_off_url"

DATA_STORE = "store"
DATA_HTTP_VIEW_REGISTERED = "http_view_registered"
DATA_SERVICES_REGISTERED = "services_registered"
DATA_SCREEN_STATE = "screen_state"
DATA_ENTRY_DEVICE = "device"

API_BASE_PATH = "/api/website_kiosk"
API_COMMAND_PATH = f"{API_BASE_PATH}/command"
API_SETTINGS_PATH = f"{API_BASE_PATH}/settings"
