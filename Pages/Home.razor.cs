using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using WebsiteKiosk.Models;
using WebsiteKiosk.Services;

namespace WebsiteKiosk.Pages;

public partial class Home : IDisposable
{
    private const string SlideshowFrameId = "website-kiosk-frame";
    private const string TimeFormat = "HH:mm:ss";
    private const int SettingsHotspotDoubleTapThresholdMilliseconds = 500;

    [Inject] private WebsiteConfigurationStore ConfigurationStore { get; set; } = null!;
    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] private HttpClient HttpClient { get; set; } = null!;
    [Inject] private KioskEndpointService EndpointService { get; set; } = null!;
    [Inject] private KioskPayloadService PayloadService { get; set; } = null!;
    [Inject] private KioskSlideshowService SlideshowService { get; set; } = null!;
    [Inject] private KioskNavigationService NavigationService { get; set; } = null!;

    private CancellationTokenSource? _slideshowCancellation;
    private CancellationTokenSource? _integrationCancellation;
    private bool _pendingInitialNavigation;
    private bool _slideshowLoopStarted;
    private bool _wasPausedBeforeScreenOff;

    private List<WebsiteEntry> Websites { get; set; } = [];
    private int RotateFrequencySeconds { get; set; } = WebsiteKioskConfiguration.DefaultRotateFrequencySeconds;
    private int IntegrationPollFrequencySeconds { get; set; } = WebsiteKioskConfiguration.DefaultIntegrationPollFrequencySeconds;
    private int CurrentIndex { get; set; }
    private string? LastNavigatedUrl { get; set; }
    private string? ScreenOffReturnUrl { get; set; }
    private string? PendingCommandNavigationUrl { get; set; }
    private string? StartUrl { get; set; }
    private string? ScreenOffUrl { get; set; }
    private string? IntegrationBaseUrl { get; set; }
    private string? IntegrationDeviceId { get; set; }
    private string? IntegrationCommandUrl { get; set; }
    private string? IntegrationSettingsUrl { get; set; }
    private string? IntegrationAccessToken { get; set; }
    private string? LastProcessedCommandSignature { get; set; }
    private string? LastSettingsSignature { get; set; }
    private string? LastCommandReceived { get; set; }
    private string LastPollStatus { get; set; } = "Waiting for first poll...";

    private bool IsRunning { get; set; }
    private bool IsPaused { get; set; }
    private bool IsScreenOff { get; set; }
    private bool DebugLoggingEnabled { get; set; }
    private bool IsConnectionTestInProgress { get; set; }
    private bool? IsConnectionTestSuccessful { get; set; }
    private bool IsIntegrationSettingsInUse { get; set; }
    private bool ShowArrowOverlayButtons { get; set; } = true;
    private bool ShowPauseOverlayButton { get; set; } = true;
    private bool ShowSettingsOverlayButton { get; set; } = true;
    private DateTimeOffset? LastSettingsHotspotTouchEndAt { get; set; }

    private string StatusMessage { get; set; } = string.Empty;
    private string? ResolvedCommandUrlPreview => EndpointService.BuildCommandUrl(IntegrationBaseUrl, IntegrationDeviceId);
    private string CurrentPolledCommandUrl => string.IsNullOrWhiteSpace(IntegrationCommandUrl)
        ? "(not configured)"
        : IntegrationCommandUrl;

    private string DebugStatusText => string.IsNullOrWhiteSpace(LastCommandReceived)
        ? $"{LastPollStatus} | Polling: {CurrentPolledCommandUrl}"
        : $"Last command received: {LastCommandReceived} | {LastPollStatus} | Polling: {CurrentPolledCommandUrl}";

    private string ConnectButtonText => IsConnectionTestInProgress
        ? "Connecting..."
        : IsConnectionTestSuccessful is true
            ? "Connected ✓"
            : IsConnectionTestSuccessful is false
                ? "Not Connected ✗"
                : "Test Connection";

    private string ConnectButtonCssClass => IsConnectionTestSuccessful switch
    {
        true => "btn action-btn connect-status success",
        false => "btn action-btn connect-status failure",
        _ => "btn btn-outline-primary action-btn connect-status"
    };

    protected override async Task OnInitializedAsync()
    {
        var saved = await ConfigurationStore.LoadAsync();
        ApplySavedConfiguration(saved);

        StatusMessage = Websites.Count == 0
            ? "No URLs available to rotate."
            : "Click Start Slideshow to begin rotating URLs that you configured in Home Assistant.";

        await LoadIntegrationSettingsAsync();
        StartIntegrationPolling();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsRunning && EndpointService.IsValidHttpUrl(PendingCommandNavigationUrl))
        {
            await NavigateToPendingCommandUrlAsync();
            return;
        }

        if (!_pendingInitialNavigation || !IsRunning || Websites.Count == 0)
        {
            return;
        }

        _pendingInitialNavigation = false;

        try
        {
            await NavigateToCurrentUrlAsync();

            if (_slideshowCancellation is not null && !_slideshowLoopStarted)
            {
                _slideshowLoopStarted = true;
                _ = RunSlideshowAsync(_slideshowCancellation.Token);
            }
        }
        catch (JSException ex)
        {
            IsRunning = false;
            IsPaused = false;
            StatusMessage = ex.Message;
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task NavigateToPendingCommandUrlAsync()
    {
        var targetUrl = PendingCommandNavigationUrl;
        PendingCommandNavigationUrl = null;

        try
        {
            LastNavigatedUrl = await NavigationService.NavigateFrameAsync(JsRuntime, SlideshowFrameId, LastNavigatedUrl, targetUrl);
        }
        catch (JSException ex)
        {
            IsRunning = false;
            IsPaused = false;
            StatusMessage = ex.Message;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void ApplySavedConfiguration(WebsiteKioskConfiguration saved)
    {
        RotateFrequencySeconds = saved.RotateFrequencySeconds;
        IntegrationPollFrequencySeconds = saved.IntegrationPollFrequencySeconds;
        StartUrl = saved.StartUrl;
        ScreenOffUrl = saved.ScreenOffUrl;
        IntegrationCommandUrl = saved.IntegrationCommandUrl;
        IntegrationBaseUrl = saved.IntegrationBaseUrl;
        IntegrationDeviceId = saved.IntegrationDeviceId;

        if (string.IsNullOrWhiteSpace(IntegrationBaseUrl)
            && string.IsNullOrWhiteSpace(IntegrationDeviceId)
            && EndpointService.TryParseCommandUrl(saved.IntegrationCommandUrl, out var parsedBaseUrl, out var parsedDeviceId))
        {
            IntegrationBaseUrl = parsedBaseUrl;
            IntegrationDeviceId = parsedDeviceId;
        }
        else if (string.IsNullOrWhiteSpace(IntegrationBaseUrl))
        {
            IntegrationBaseUrl = saved.IntegrationCommandUrl;
        }

        IntegrationSettingsUrl = EndpointService.BuildSettingsUrl(saved.IntegrationCommandUrl);
        IntegrationAccessToken = saved.IntegrationAccessToken;
        DebugLoggingEnabled = saved.DebugLoggingEnabled;
        ShowArrowOverlayButtons = saved.ShowArrowOverlayButtons;
        ShowPauseOverlayButton = saved.ShowPauseOverlayButton;
        ShowSettingsOverlayButton = saved.ShowSettingsOverlayButton;

        Websites = saved.Websites
            .Where(x => EndpointService.IsValidHttpUrl(x.Url))
            .OrderBy(x => x.Order)
            .Select(x => new WebsiteEntry
            {
                Url = x.Url.Trim(),
                Order = x.Order,
            })
            .ToList();
    }

    private async Task StartSlideshowAsync()
    {
        var resolvedCommandUrl = EndpointService.ResolveCommandUrlInput(IntegrationBaseUrl, IntegrationDeviceId) ?? IntegrationCommandUrl;
        var startPlan = SlideshowService.PlanStart(IsRunning, resolvedCommandUrl, IntegrationCommandUrl, Websites.Count);

        if (!startPlan.CanStart)
        {
            if (!string.IsNullOrWhiteSpace(startPlan.ValidationMessage))
            {
                StatusMessage = startPlan.ValidationMessage;
            }
            return;
        }

        if (startPlan.ShouldRefreshEndpoints && !string.IsNullOrWhiteSpace(startPlan.ResolvedCommandUrl))
        {
            IntegrationCommandUrl = startPlan.ResolvedCommandUrl;
            IntegrationSettingsUrl = EndpointService.BuildSettingsUrl(startPlan.ResolvedCommandUrl);
            LastProcessedCommandSignature = null;
            LastSettingsSignature = null;

            await LoadIntegrationSettingsAsync();
            StartIntegrationPolling();
        }

        ApplySlideshowState(SlideshowService.CreateStartedState());
        _slideshowCancellation = new CancellationTokenSource();

        await InvokeAsync(StateHasChanged);
    }

    private async Task RunSlideshowAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(RotateFrequencySeconds), token);

                if (IsPaused)
                {
                    continue;
                }

                CurrentIndex = SlideshowService.GetNextIndex(CurrentIndex, Websites.Count);
                await NavigateToCurrentUrlAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (JSException ex)
        {
            await InvokeAsync(() =>
            {
                IsRunning = false;
                StatusMessage = ex.Message;
                StateHasChanged();
            });
        }
    }

    private Task StopSlideshowAsync()
    {
        _slideshowCancellation?.Cancel();
        _slideshowCancellation?.Dispose();
        _slideshowCancellation = null;
        ApplySlideshowState(SlideshowService.CreateStoppedState());

        return Task.CompletedTask;
    }

    private async Task GoPreviousAsync()
    {
        if (!SlideshowService.CanNavigateManually(IsRunning, Websites.Count))
        {
            return;
        }

        CurrentIndex = SlideshowService.GetPreviousIndex(CurrentIndex, Websites.Count);
        await NavigateToCurrentUrlAsync();
    }

    private async Task GoNextAsync()
    {
        if (!SlideshowService.CanNavigateManually(IsRunning, Websites.Count))
        {
            return;
        }

        CurrentIndex = SlideshowService.GetNextIndex(CurrentIndex, Websites.Count);
        await NavigateToCurrentUrlAsync();
    }

    private void TogglePause()
    {
        if (!SlideshowService.TryTogglePause(IsRunning, IsPaused, out var newPaused))
        {
            return;
        }

        IsPaused = newPaused;
    }

    private void StartIntegrationPolling()
    {
        _integrationCancellation?.Cancel();
        _integrationCancellation?.Dispose();
        _integrationCancellation = null;

        if (string.IsNullOrWhiteSpace(IntegrationCommandUrl))
        {
            return;
        }

        _integrationCancellation = new CancellationTokenSource();
        _ = RunIntegrationPollingAsync(_integrationCancellation.Token);
    }

    private async Task RunIntegrationPollingAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await LoadIntegrationSettingsAsync(token);

                var command = await FetchIntegrationCommandAsync(token);
                if (command is not null)
                {
                    LastCommandReceived = string.IsNullOrWhiteSpace(command.Command)
                        ? TimestampNow()
                        : $"{command.Command} ({TimestampNow()})";
                    SetPollStatus("Command received");

                    await InvokeAsync(async () =>
                    {
                        await HandleIntegrationCommandAsync(command);
                        StateHasChanged();
                    });
                }
                else
                {
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                SetPollStatus($"Command poll failed: {ex.GetType().Name}");
                await InvokeAsync(StateHasChanged);
            }

            await Task.Delay(TimeSpan.FromSeconds(IntegrationPollFrequencySeconds), token);
        }
    }

    private async Task<KioskIntegrationCommand?> FetchIntegrationCommandAsync(CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(IntegrationCommandUrl))
        {
            return null;
        }

        var commandUrl = EndpointService.AddAccessTokenQuery(IntegrationCommandUrl, IntegrationAccessToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, EndpointService.BuildNoCacheUrl(commandUrl));

        HttpResponseMessage response;
        try
        {
            response = await HttpClient.SendAsync(request, token);
        }
        catch (Exception ex)
        {
            SetPollStatus($"Command poll request failed: {ex.GetType().Name}");
            return null;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                SetPollStatus($"Command poll HTTP {(int)response.StatusCode}");
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(token);
            if (string.IsNullOrWhiteSpace(json))
            {
                SetPollStatus("Command poll empty body");
                return null;
            }

            var command = PayloadService.ParseCommand(json);
            if (command is null)
            {
                SetPollStatus("Command poll no command");
                return null;
            }

            var signature = string.IsNullOrWhiteSpace(command.Id)
                ? json.Trim()
                : command.Id.Trim();

            if (string.Equals(signature, LastProcessedCommandSignature, StringComparison.Ordinal))
            {
                SetPollStatus("Command poll duplicate ignored");
                return null;
            }

            LastProcessedCommandSignature = signature;
            return command;
        }
    }

    private async Task LoadIntegrationSettingsAsync(CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(IntegrationSettingsUrl))
        {
            IsIntegrationSettingsInUse = false;
            return;
        }

        var settingsUrl = EndpointService.AddAccessTokenQuery(IntegrationSettingsUrl, IntegrationAccessToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, EndpointService.BuildNoCacheUrl(settingsUrl));

        HttpResponseMessage response;
        try
        {
            response = await HttpClient.SendAsync(request, token);
        }
        catch
        {
            SetPollStatus("Settings sync failed");
            return;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                SetPollStatus($"Settings HTTP {(int)response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync(token);
            if (string.IsNullOrWhiteSpace(json))
            {
                SetPollStatus("Settings empty body");
                return;
            }

            var signature = json.Trim();
            if (string.Equals(signature, LastSettingsSignature, StringComparison.Ordinal))
            {
                IsIntegrationSettingsInUse = true;
                return;
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                SetPollStatus("Settings invalid payload");
                return;
            }

            ApplyIntegrationSettings(doc.RootElement);
            LastSettingsSignature = signature;
            IsIntegrationSettingsInUse = true;

            if (DebugLoggingEnabled)
            {
                LastPollStatus = $"Settings sync ok: {Websites.Count} URLs ({TimestampNow()})";
            }
        }
    }

    private async Task RefreshIntegrationSettingsAsync()
    {
        LastSettingsSignature = null;
        await LoadIntegrationSettingsAsync();
        await InvokeAsync(StateHasChanged);
    }

    private void ApplyIntegrationSettings(JsonElement root)
    {
        var rotateSeconds = PayloadService.ParseRotateFrequencySeconds(root);
        if (rotateSeconds.HasValue)
        {
            RotateFrequencySeconds = rotateSeconds.Value;
        }

        var loadedWebsites = PayloadService.ParseWebsites(root);
        if (HasProperty(root, "websites"))
        {
            Websites = loadedWebsites;
            if (CurrentIndex >= Websites.Count && Websites.Count > 0)
            {
                CurrentIndex = 0;
            }

            StatusMessage = Websites.Count == 0
                ? "No URLs available to rotate."
                : "Click Start Slideshow to begin rotating URLs.";
        }

        var startUrl = PayloadService.ParseStartUrl(root);
        StartUrl = EndpointService.IsValidHttpUrl(startUrl) ? startUrl : null;

        var screenOffUrl = PayloadService.ParseScreenOffUrl(root);
        ScreenOffUrl = EndpointService.IsValidHttpUrl(screenOffUrl) ? screenOffUrl : null;
    }

    private static bool HasProperty(JsonElement root, string propertyName)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals(propertyName) || property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }


    private async Task HandleIntegrationCommandAsync(KioskIntegrationCommand command)
    {
        switch (PayloadService.NormalizeCommandType(command.Command))
        {
            case KioskCommandType.LoadUrl:
                await NavigateToExplicitUrlAsync(command.Url);
                break;
            case KioskCommandType.LoadStartUrl:
                await NavigateToStartUrlAsync();
                break;
            case KioskCommandType.ScreenSwitch:
                ApplyScreenSwitch(command);
                break;
            case KioskCommandType.StartApplication:
                await JsRuntime.InvokeVoidAsync("websiteKiosk.startApplication", command.AppIntent, command.PackageName, command.DeepLinkUrl);
                break;
        }
    }

    private void SetPollStatus(string message)
    {
        LastPollStatus = $"{message} ({TimestampNow()})";
    }

    private static string TimestampNow() => DateTimeOffset.Now.ToString(TimeFormat);


    private void ApplyScreenSwitch(KioskIntegrationCommand command)
    {
        var isOn = command.Enabled ?? PayloadService.ParseBoolean(command.Value) ?? true;
        var transition = SlideshowService.BuildScreenSwitchTransition(
            isOn,
            IsPaused,
            _wasPausedBeforeScreenOff,
            LastNavigatedUrl,
            ScreenOffUrl,
            ScreenOffReturnUrl,
            EndpointService.IsValidHttpUrl);

        IsScreenOff = transition.IsScreenOff;
        IsPaused = transition.IsPaused;
        _wasPausedBeforeScreenOff = transition.WasPausedBeforeScreenOff;
        ScreenOffReturnUrl = transition.ScreenOffReturnUrl;

        if (transition.ForceRunning)
        {
            IsRunning = true;
        }

        if (EndpointService.IsValidHttpUrl(transition.NavigateToUrl))
        {
            _ = NavigateToExplicitUrlAsync(transition.NavigateToUrl);
        }
    }

    private async Task NavigateToStartUrlAsync()
    {
        await LoadIntegrationSettingsAsync();

        var target = NavigationService.ResolveStartTarget(StartUrl, Websites, EndpointService.IsValidHttpUrl);
        if (!EndpointService.IsValidHttpUrl(target))
        {
            StatusMessage = "Load Start URL requested, but no valid Start URL or rotation URL is available.";
            return;
        }

        await NavigateToExplicitUrlAsync(target);
    }

    private async Task NavigateToExplicitUrlAsync(string? targetUrl)
    {
        if (!EndpointService.IsValidHttpUrl(targetUrl))
        {
            return;
        }

        IsScreenOff = false;

        var matchingIndex = NavigationService.FindMatchingIndex(Websites, targetUrl);
        if (matchingIndex >= 0)
        {
            CurrentIndex = matchingIndex;
        }

        if (!IsRunning)
        {
            IsRunning = true;
            IsPaused = false;
            PendingCommandNavigationUrl = targetUrl;
            await InvokeAsync(StateHasChanged);
            return;
        }

        LastNavigatedUrl = await NavigationService.NavigateFrameAsync(JsRuntime, SlideshowFrameId, LastNavigatedUrl, targetUrl);
    }


    private async Task ConnectToHomeAssistantAsync()
    {
        var resolvedCommandUrl = EndpointService.ResolveCommandUrlInput(IntegrationBaseUrl, IntegrationDeviceId);
        if (string.IsNullOrWhiteSpace(resolvedCommandUrl))
        {
            IsConnectionTestSuccessful = false;
            StatusMessage = "Enter a valid Home Assistant base URL and device identifier before connecting.";
            return;
        }

        IntegrationCommandUrl = resolvedCommandUrl;
        IntegrationSettingsUrl = EndpointService.BuildSettingsUrl(resolvedCommandUrl);
        if (string.IsNullOrWhiteSpace(IntegrationSettingsUrl))
        {
            IsConnectionTestSuccessful = false;
            StatusMessage = "Unable to build Home Assistant settings endpoint from the current values.";
            return;
        }

        IsConnectionTestInProgress = true;
        StatusMessage = "Connecting to Home Assistant...";

        try
        {
            var integrationProbeUrls = new[] { IntegrationSettingsUrl, resolvedCommandUrl }
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var checkUrl in integrationProbeUrls)
            {
                var probeUrl = EndpointService.AddAccessTokenQuery(checkUrl, IntegrationAccessToken);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, EndpointService.BuildNoCacheUrl(probeUrl));
                    using var response = await HttpClient.SendAsync(request);

                    IsConnectionTestSuccessful = true;
                    StatusMessage = response.IsSuccessStatusCode
                        ? $"Connected to Home Assistant integration endpoint: {checkUrl}"
                        : $"Integration endpoint reached ({(int)response.StatusCode} {response.ReasonPhrase}): {checkUrl}";
                    return;
                }
                catch
                {
                    // ignored
                }
            }

            var baseUrl = EndpointService.NormalizeOptionalHttpUrl(IntegrationBaseUrl);
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, EndpointService.BuildNoCacheUrl(baseUrl));
                    using var response = await HttpClient.SendAsync(request);

                    IsConnectionTestSuccessful = false;
                    StatusMessage = $"Base URL is reachable but integration endpoints are not. Check device id/path/CORS. Base response: {(int)response.StatusCode} {response.ReasonPhrase}.";
                    return;
                }
                catch
                {
                    // ignored
                }
            }

            IsConnectionTestSuccessful = false;
            StatusMessage = "Connection failed. Check HA URL, CORS origin, and HTTPS certificate trust on this device.";
        }
        finally
        {
            IsConnectionTestInProgress = false;
        }
    }

    private void ResetConnectionTestState()
    {
        IsConnectionTestSuccessful = null;
    }

    private void OnIntegrationBaseUrlInput(ChangeEventArgs args)
    {
        IntegrationBaseUrl = args.Value?.ToString();
        ResetConnectionTestState();
    }

    private void OnIntegrationDeviceIdInput(ChangeEventArgs args)
    {
        IntegrationDeviceId = args.Value?.ToString();
        ResetConnectionTestState();
    }

    private void OnIntegrationAccessTokenInput(ChangeEventArgs args)
    {
        IntegrationAccessToken = args.Value?.ToString();
        ResetConnectionTestState();
    }

    private async Task SaveIntegrationSettingsAsync()
    {
        try
        {
            var configuration = await ConfigurationStore.LoadAsync();

            configuration.IntegrationBaseUrl = string.IsNullOrWhiteSpace(IntegrationBaseUrl)
                ? null
                : IntegrationBaseUrl.Trim();
            configuration.IntegrationDeviceId = string.IsNullOrWhiteSpace(IntegrationDeviceId)
                ? null
                : IntegrationDeviceId.Trim();

            var resolvedCommandUrl = EndpointService.ResolveCommandUrlInput(IntegrationBaseUrl, IntegrationDeviceId);
            if (string.IsNullOrWhiteSpace(resolvedCommandUrl))
            {
                await ConfigurationStore.SaveAsync(configuration);
                StatusMessage = "Enter a valid Home Assistant base URL and device identifier.";
                return;
            }

            if (EndpointService.TryParseCommandUrl(resolvedCommandUrl, out var parsedBaseUrl, out var parsedDeviceId))
            {
                IntegrationBaseUrl = parsedBaseUrl;
                IntegrationDeviceId = parsedDeviceId;
            }

            configuration.IntegrationCommandUrl = resolvedCommandUrl;
            configuration.IntegrationAccessToken = string.IsNullOrWhiteSpace(IntegrationAccessToken)
                ? null
                : IntegrationAccessToken.Trim();
            configuration.IntegrationPollFrequencySeconds = IntegrationPollFrequencySeconds <= 0
                ? WebsiteKioskConfiguration.DefaultIntegrationPollFrequencySeconds
                : IntegrationPollFrequencySeconds;
            configuration.DebugLoggingEnabled = DebugLoggingEnabled;
            configuration.ShowArrowOverlayButtons = ShowArrowOverlayButtons;
            configuration.ShowPauseOverlayButton = ShowPauseOverlayButton;
            configuration.ShowSettingsOverlayButton = ShowSettingsOverlayButton;

            await ConfigurationStore.SaveAsync(configuration);

            var reloadedConfiguration = await ConfigurationStore.LoadAsync();
            if (!string.Equals(reloadedConfiguration.IntegrationCommandUrl, configuration.IntegrationCommandUrl, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = "Save failed: browser storage did not persist the updated integration settings.";
                return;
            }

            IntegrationCommandUrl = configuration.IntegrationCommandUrl;
            IntegrationAccessToken = configuration.IntegrationAccessToken;
            IntegrationPollFrequencySeconds = configuration.IntegrationPollFrequencySeconds;
            DebugLoggingEnabled = configuration.DebugLoggingEnabled;
            ShowArrowOverlayButtons = configuration.ShowArrowOverlayButtons;
            ShowPauseOverlayButton = configuration.ShowPauseOverlayButton;
            ShowSettingsOverlayButton = configuration.ShowSettingsOverlayButton;
            IntegrationSettingsUrl = EndpointService.BuildSettingsUrl(configuration.IntegrationCommandUrl);
            LastProcessedCommandSignature = null;
            LastSettingsSignature = null;

            try
            {
                await LoadIntegrationSettingsAsync();
            }
            catch
            {
                StatusMessage = "Settings saved, but Home Assistant could not be reached. Check URL/CORS and try again.";
            }

            StartIntegrationPolling();
            if (!StatusMessage.StartsWith("Settings saved, but", StringComparison.Ordinal))
            {
                StatusMessage = $"Integration settings saved: {resolvedCommandUrl} ({TimestampNow()})";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Save failed: {ex.Message}";
        }
    }

    private async Task NavigateToCurrentUrlAsync()
    {
        if (Websites.Count == 0)
        {
            return;
        }

        var currentUrl = Websites[CurrentIndex].Url;
        LastNavigatedUrl = await NavigationService.NavigateFrameAsync(JsRuntime, SlideshowFrameId, LastNavigatedUrl, currentUrl);
    }

    private void ApplySlideshowState(SlideshowState state)
    {
        IsRunning = state.IsRunning;
        IsPaused = state.IsPaused;
        IsScreenOff = state.IsScreenOff;
        _pendingInitialNavigation = state.PendingInitialNavigation;
        _slideshowLoopStarted = state.SlideshowLoopStarted;
        if (state.CurrentIndex.HasValue)
        {
            CurrentIndex = state.CurrentIndex.Value;
        }
        LastNavigatedUrl = state.LastNavigatedUrl;
        ScreenOffReturnUrl = state.ScreenOffReturnUrl;
        StatusMessage = state.StatusMessage;
    }

    private async Task OnSettingsHotspotTouchEnd(TouchEventArgs _)
    {
        if (ShowSettingsOverlayButton || !IsRunning)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (LastSettingsHotspotTouchEndAt.HasValue
            && (now - LastSettingsHotspotTouchEndAt.Value).TotalMilliseconds <= SettingsHotspotDoubleTapThresholdMilliseconds)
        {
            LastSettingsHotspotTouchEndAt = null;
            await StopSlideshowAsync();
            return;
        }

        LastSettingsHotspotTouchEndAt = now;
    }

    private async Task OnSettingsHotspotDoubleClick()
    {
        if (ShowSettingsOverlayButton || !IsRunning)
        {
            return;
        }

        await StopSlideshowAsync();
    }

    public void Dispose()
    {
        _slideshowCancellation?.Cancel();
        _slideshowCancellation?.Dispose();
        _integrationCancellation?.Cancel();
        _integrationCancellation?.Dispose();
    }
}
