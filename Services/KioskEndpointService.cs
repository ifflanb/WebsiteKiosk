namespace WebsiteKiosk.Services;

public sealed class KioskEndpointService
{
    public bool IsValidHttpUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public string? NormalizeOptionalHttpUrl(string? value)
    {
        var candidate = value?.Trim();
        return IsValidHttpUrl(candidate) ? candidate : null;
    }

    public string? ResolveCommandUrlInput(string? baseUrlOrEndpoint, string? deviceId)
    {
        var normalizedInput = NormalizeOptionalHttpUrl(baseUrlOrEndpoint);
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return null;
        }

        if (TryParseCommandUrl(normalizedInput, out var parsedBaseUrl, out var parsedDeviceId))
        {
            var effectiveDeviceId = string.IsNullOrWhiteSpace(deviceId) ? parsedDeviceId : deviceId.Trim();
            return BuildCommandUrl(parsedBaseUrl, effectiveDeviceId);
        }

        return BuildCommandUrl(normalizedInput, deviceId);
    }

    public string? BuildCommandUrl(string? baseUrl, string? deviceId)
    {
        var normalizedBaseUrl = NormalizeOptionalHttpUrl(baseUrl);
        var normalizedDeviceId = deviceId?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedBaseUrl) || string.IsNullOrWhiteSpace(normalizedDeviceId))
        {
            return null;
        }

        if (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Path = CombinePaths(uri.AbsolutePath, $"api/website_kiosk/command/{Uri.EscapeDataString(normalizedDeviceId)}"),
        };

        return builder.Uri.ToString();
    }

    public bool TryParseCommandUrl(string? commandUrl, out string? baseUrl, out string? deviceId)
    {
        baseUrl = null;
        deviceId = null;

        if (!Uri.TryCreate(commandUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        const string marker = "/api/website_kiosk/command/";
        var markerIndex = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        var encodedDeviceId = path[(markerIndex + marker.Length)..].Trim('/');
        if (string.IsNullOrWhiteSpace(encodedDeviceId))
        {
            return false;
        }

        var prefixPath = path[..markerIndex].TrimEnd('/');
        var builder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Path = string.IsNullOrWhiteSpace(prefixPath) ? "/" : prefixPath,
        };

        baseUrl = builder.Uri.ToString().TrimEnd('/');
        deviceId = Uri.UnescapeDataString(encodedDeviceId);
        return true;
    }

    public string? BuildSettingsUrl(string? commandUrl)
    {
        if (!Uri.TryCreate(commandUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var path = uri.AbsolutePath;
        var marker = "/command/";
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var settingsPath = string.Concat(path.AsSpan(0, index), "/settings/", path.AsSpan(index + marker.Length));
        var builder = new UriBuilder(uri) { Path = settingsPath };

        return builder.Uri.ToString();
    }

    public string BuildNoCacheUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var builder = new UriBuilder(uri);
        var existingQuery = builder.Query.TrimStart('?');
        var cacheBuster = $"_ts={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        builder.Query = string.IsNullOrWhiteSpace(existingQuery)
            ? cacheBuster
            : $"{existingQuery}&{cacheBuster}";

        return builder.Uri.ToString();
    }

    public string AddAccessTokenQuery(string url, string? accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        var builder = new UriBuilder(uri);
        var query = builder.Query.TrimStart('?');
        var tokenPart = $"access_token={Uri.EscapeDataString(accessToken.Trim())}";
        builder.Query = string.IsNullOrWhiteSpace(query)
            ? tokenPart
            : $"{query}&{tokenPart}";

        return builder.Uri.ToString();
    }

    private static string CombinePaths(string basePath, string relativePath)
    {
        var left = string.IsNullOrWhiteSpace(basePath) ? string.Empty : basePath.TrimEnd('/');
        var right = relativePath.TrimStart('/');

        return string.IsNullOrEmpty(left)
            ? $"/{right}"
            : $"{left}/{right}";
    }
}
