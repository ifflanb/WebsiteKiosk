using System.Text.Json;
using Microsoft.JSInterop;
using WebsiteKiosk.Models;

namespace WebsiteKiosk.Services;

public sealed class WebsiteConfigurationStore(IJSRuntime jsRuntime, HttpClient httpClient)
{
    private const string StorageKey = "website-kiosk-config";
    private const string DefaultConfigPath = "website-kiosk-config.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public async Task<WebsiteKioskConfiguration> LoadAsync()
    {
        string? json;

        try
        {
            json = await jsRuntime.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        }
        catch (JSException)
        {
            json = null;
        }
        catch (InvalidOperationException)
        {
            json = null;
        }

        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                return ParseJson(json);
            }
            catch (JsonException)
            {
            }
        }

        var defaultConfiguration = await LoadDefaultFromFileAsync();
        if (defaultConfiguration.Websites.Count > 0)
        {
            return defaultConfiguration;
        }

        return new WebsiteKioskConfiguration();
    }

    private async Task<WebsiteKioskConfiguration> LoadDefaultFromFileAsync()
    {
        try
        {
            var fileJson = await httpClient.GetStringAsync(DefaultConfigPath);
            if (string.IsNullOrWhiteSpace(fileJson))
            {
                return new WebsiteKioskConfiguration();
            }

            return ParseJson(fileJson);
        }
        catch (HttpRequestException)
        {
            return new WebsiteKioskConfiguration();
        }
        catch (TaskCanceledException)
        {
            return new WebsiteKioskConfiguration();
        }
        catch (JsonException)
        {
            return new WebsiteKioskConfiguration();
        }
    }

    public async Task SaveAsync(WebsiteKioskConfiguration configuration)
    {
        var json = ToJson(configuration);

        try
        {
            await jsRuntime.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    public string ToJson(WebsiteKioskConfiguration configuration)
    {
        return JsonSerializer.Serialize(Normalize(configuration), JsonOptions);
    }

    public WebsiteKioskConfiguration ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var entries = JsonSerializer.Deserialize<List<WebsiteEntry>>(json, JsonOptions) ?? [];
            return new WebsiteKioskConfiguration
            {
                Websites = entries
            };
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            var configuration = JsonSerializer.Deserialize<WebsiteKioskConfiguration>(json, JsonOptions)
                ?? new WebsiteKioskConfiguration();

            return Normalize(configuration);
        }

        return new WebsiteKioskConfiguration();
    }

    private static WebsiteKioskConfiguration Normalize(WebsiteKioskConfiguration configuration)
    {
        configuration.Websites ??= [];
        configuration.StartUrl = string.IsNullOrWhiteSpace(configuration.StartUrl)
            ? null
            : configuration.StartUrl.Trim();
        configuration.ScreenOffUrl = string.IsNullOrWhiteSpace(configuration.ScreenOffUrl)
            ? null
            : configuration.ScreenOffUrl.Trim();
        configuration.IntegrationCommandUrl = string.IsNullOrWhiteSpace(configuration.IntegrationCommandUrl)
            ? null
            : configuration.IntegrationCommandUrl.Trim();
        configuration.IntegrationAccessToken = string.IsNullOrWhiteSpace(configuration.IntegrationAccessToken)
            ? null
            : configuration.IntegrationAccessToken.Trim();

        if (configuration.RotateFrequencySeconds <= 0)
        {
            configuration.RotateFrequencySeconds = WebsiteKioskConfiguration.DefaultRotateFrequencySeconds;
        }

        if (configuration.IntegrationPollFrequencySeconds <= 0)
        {
            configuration.IntegrationPollFrequencySeconds = WebsiteKioskConfiguration.DefaultIntegrationPollFrequencySeconds;
        }

        return configuration;
    }
}