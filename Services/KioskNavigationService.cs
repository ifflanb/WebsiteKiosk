using Microsoft.JSInterop;
using WebsiteKiosk.Models;

namespace WebsiteKiosk.Services;

public sealed class KioskNavigationService
{
    public int FindMatchingIndex(IReadOnlyList<WebsiteEntry> websites, string? targetUrl)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
        {
            return -1;
        }

        for (var i = 0; i < websites.Count; i++)
        {
            if (string.Equals(websites[i].Url, targetUrl, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public string? ResolveStartTarget(string? startUrl, IReadOnlyList<WebsiteEntry> websites, Func<string?, bool> isValidUrl)
    {
        if (isValidUrl(startUrl))
        {
            return startUrl;
        }

        return websites.FirstOrDefault()?.Url;
    }

    public async Task<string?> NavigateFrameAsync(IJSRuntime jsRuntime, string frameId, string? previousUrl, string? targetUrl)
    {
        await jsRuntime.InvokeVoidAsync("websiteKiosk.navigateFrame", frameId, previousUrl, targetUrl);
        return targetUrl;
    }
}
