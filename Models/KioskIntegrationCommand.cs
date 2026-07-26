namespace WebsiteKiosk.Models;

public sealed class KioskIntegrationCommand
{
    public string? Id { get; set; }

    public string? Command { get; set; }

    public string? Url { get; set; }

    public string? Value { get; set; }

    public bool? Enabled { get; set; }

    public string? AppIntent { get; set; }

    public string? PackageName { get; set; }

    public string? DeepLinkUrl { get; set; }
}
