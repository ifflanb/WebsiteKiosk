namespace WebsiteKiosk.Models;

public sealed class WebsiteKioskConfiguration
{
    public const int DefaultRotateFrequencySeconds = 30;
    public const int DefaultIntegrationPollFrequencySeconds = 5;

    public List<WebsiteEntry> Websites { get; set; } = [];

    public int RotateFrequencySeconds { get; set; } = DefaultRotateFrequencySeconds;

    public bool IsKioskMode { get; set; }

    public string? StartUrl { get; set; }

    public string? ScreenOffUrl { get; set; }

    public string? IntegrationBaseUrl { get; set; }

    public string? IntegrationDeviceId { get; set; }

    public string? IntegrationCommandUrl { get; set; }

    public string? IntegrationAccessToken { get; set; }

    public int IntegrationPollFrequencySeconds { get; set; } = DefaultIntegrationPollFrequencySeconds;

    public bool DebugLoggingEnabled { get; set; }

    public bool ShowArrowOverlayButtons { get; set; } = true;

    public bool ShowPauseOverlayButton { get; set; } = true;

    public bool ShowSettingsOverlayButton { get; set; } = true;
}
