using System.Text.Json;
using WebsiteKiosk.Models;

namespace WebsiteKiosk.Services;

public sealed class KioskPayloadService
{
    public KioskIntegrationCommand? ParseCommand(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    return MapCommand(item);
                }
            }

            return null;
        }

        return doc.RootElement.ValueKind == JsonValueKind.Object
            ? MapCommand(doc.RootElement)
            : null;
    }

    public KioskCommandType NormalizeCommandType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return KioskCommandType.Unknown;
        }

        var normalized = value.Trim().ToLowerInvariant().Replace('.', '_').Replace('-', '_').Replace(' ', '_');

        var mapped = normalized switch
        {
            "kiosk_load_url" or "load_url" or "load_ur1" => "load_url",
            "tablet_load_start_url" or "button_tablet_load_start_url" or "load_start_url" => "load_start_url",
            "tablet_screen" or "switch_tablet_screen" or "screen" => "screen_switch",
            "kiosk_start_application" or "start_application" => "start_application",
            _ => normalized,
        };

        if (mapped == normalized)
        {
            if (normalized.Contains("load_start_url", StringComparison.Ordinal))
            {
                return KioskCommandType.LoadStartUrl;
            }

            if (normalized.Contains("load_url", StringComparison.Ordinal))
            {
                return KioskCommandType.LoadUrl;
            }

            if (normalized.Contains("start_application", StringComparison.Ordinal))
            {
                return KioskCommandType.StartApplication;
            }

            if (normalized.Contains("tablet_screen", StringComparison.Ordinal)
                || normalized.Contains("screen_switch", StringComparison.Ordinal))
            {
                return KioskCommandType.ScreenSwitch;
            }

            return KioskCommandType.Unknown;
        }

        return mapped switch
        {
            "load_url" => KioskCommandType.LoadUrl,
            "load_start_url" => KioskCommandType.LoadStartUrl,
            "screen_switch" => KioskCommandType.ScreenSwitch,
            "start_application" => KioskCommandType.StartApplication,
            _ => KioskCommandType.Unknown,
        };
    }

    public bool? ParseBoolean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "on" or "1" => true,
            "false" or "off" or "0" => false,
            _ => null,
        };
    }

    public int? ParseRotateFrequencySeconds(JsonElement root)
    {
        if (TryGetProperty(root, "rotate_frequency_seconds", out var rotate)
            && rotate.ValueKind == JsonValueKind.Number
            && rotate.TryGetInt32(out var rotateSeconds)
            && rotateSeconds > 0)
        {
            return rotateSeconds;
        }

        return null;
    }

    public List<WebsiteEntry> ParseWebsites(JsonElement root)
    {
        var loadedWebsites = new List<WebsiteEntry>();
        if (!TryGetProperty(root, "websites", out var websitesElement))
        {
            return loadedWebsites;
        }

        if (websitesElement.ValueKind == JsonValueKind.String)
        {
            var multiline = websitesElement.GetString();
            if (string.IsNullOrWhiteSpace(multiline))
            {
                return loadedWebsites;
            }

            var index = 1;
            foreach (var line in multiline.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                loadedWebsites.Add(new WebsiteEntry { Url = line, Order = index++ });
            }

            return loadedWebsites.OrderBy(x => x.Order).ToList();
        }

        if (websitesElement.ValueKind != JsonValueKind.Array)
        {
            return loadedWebsites;
        }

        var fallbackOrder = 1;
        foreach (var item in websitesElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var rawUrl = item.GetString();
                if (!string.IsNullOrWhiteSpace(rawUrl))
                {
                    loadedWebsites.Add(new WebsiteEntry { Url = rawUrl.Trim(), Order = fallbackOrder++ });
                }

                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var url = GetString(item, "url");
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var order = fallbackOrder;
            if (TryGetProperty(item, "order", out var orderElement)
                && orderElement.ValueKind == JsonValueKind.Number
                && orderElement.TryGetInt32(out var parsedOrder)
                && parsedOrder > 0)
            {
                order = parsedOrder;
            }

            loadedWebsites.Add(new WebsiteEntry { Url = url.Trim(), Order = order });
            fallbackOrder++;
        }

        return loadedWebsites.OrderBy(x => x.Order).ToList();
    }

    public string? ParseStartUrl(JsonElement root) => GetString(root, "start_url");

    public string? ParseScreenOffUrl(JsonElement root) => GetString(root, "screen_off_url");

    private KioskIntegrationCommand MapCommand(JsonElement root)
    {
        var payload = TryGetProperty(root, "data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Object
            ? dataElement
            : root;

        return new KioskIntegrationCommand
        {
            Id = GetString(root, "id", "commandId", "event_id") ?? GetString(payload, "id", "commandId", "event_id"),
            Command = GetString(root, "command", "service", "action", "name") ?? GetString(payload, "command", "service", "action", "name"),
            Url = GetString(payload, "url", "uri", "targetUrl"),
            Value = GetString(payload, "value", "state"),
            Enabled = GetBool(payload, "enabled", "isOn", "on"),
            AppIntent = GetString(payload, "appIntent", "intent", "intentUrl"),
            PackageName = GetString(payload, "packageName", "package"),
            DeepLinkUrl = GetString(payload, "deepLinkUrl", "deepLink", "appUrl"),
        };
    }

    private static string? GetString(JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetProperty(element, key, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }

            if (property.ValueKind == JsonValueKind.Number || property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
            {
                return property.ToString();
            }
        }

        return null;
    }

    private bool? GetBool(JsonElement element, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetProperty(element, key, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (property.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (property.ValueKind == JsonValueKind.String)
            {
                return ParseBoolean(property.GetString());
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.NameEquals(propertyName) || property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

}
