namespace WebsiteKiosk.Services;

public sealed class KioskSlideshowService
{
    public SlideshowStartPlan PlanStart(
        bool isRunning,
        string? resolvedCommandUrl,
        string? currentCommandUrl,
        int websitesCount)
    {
        if (isRunning)
        {
            return new SlideshowStartPlan(false, null, false, null);
        }

        if (string.IsNullOrWhiteSpace(resolvedCommandUrl))
        {
            return new SlideshowStartPlan(false, "Enter a Home Assistant base URL and device identifier before starting the slideshow.", false, null);
        }

        var shouldRefreshEndpoints = !string.Equals(currentCommandUrl, resolvedCommandUrl, StringComparison.OrdinalIgnoreCase);

        if (websitesCount == 0)
        {
            return new SlideshowStartPlan(false, "No rotation URLs are available yet. Configure them in the Home Assistant integration settings.", shouldRefreshEndpoints, resolvedCommandUrl);
        }

        return new SlideshowStartPlan(true, null, shouldRefreshEndpoints, resolvedCommandUrl);
    }

    public SlideshowState CreateStartedState() => new(
        IsRunning: true,
        IsPaused: false,
        IsScreenOff: false,
        PendingInitialNavigation: true,
        SlideshowLoopStarted: false,
        CurrentIndex: 0,
        LastNavigatedUrl: null,
        ScreenOffReturnUrl: null,
        StatusMessage: "Slideshow running.");

    public SlideshowState CreateStoppedState() => new(
        IsRunning: false,
        IsPaused: false,
        IsScreenOff: false,
        PendingInitialNavigation: false,
        SlideshowLoopStarted: false,
        CurrentIndex: null,
        LastNavigatedUrl: null,
        ScreenOffReturnUrl: null,
        StatusMessage: "Slideshow stopped.");

    public bool CanNavigateManually(bool isRunning, int websitesCount) => isRunning && websitesCount > 0;

    public bool TryTogglePause(bool isRunning, bool currentPaused, out bool newPaused)
    {
        if (!isRunning)
        {
            newPaused = currentPaused;
            return false;
        }

        newPaused = !currentPaused;
        return true;
    }

    public ScreenSwitchTransition BuildScreenSwitchTransition(
        bool isOn,
        bool currentIsPaused,
        bool wasPausedBeforeScreenOff,
        string? lastNavigatedUrl,
        string? screenOffUrl,
        string? screenOffReturnUrl,
        Func<string?, bool> isValidUrl)
    {
        if (!isOn)
        {
            var navigateTo = isValidUrl(screenOffUrl) ? screenOffUrl : null;
            return new ScreenSwitchTransition(
                IsScreenOff: navigateTo is null,
                IsPaused: true,
                WasPausedBeforeScreenOff: currentIsPaused,
                ScreenOffReturnUrl: lastNavigatedUrl,
                NavigateToUrl: navigateTo,
                ForceRunning: navigateTo is null);
        }

        var returnTarget = isValidUrl(screenOffReturnUrl) ? screenOffReturnUrl : null;
        return new ScreenSwitchTransition(
            IsScreenOff: false,
            IsPaused: wasPausedBeforeScreenOff,
            WasPausedBeforeScreenOff: wasPausedBeforeScreenOff,
            ScreenOffReturnUrl: null,
            NavigateToUrl: returnTarget,
            ForceRunning: false);
    }

    public int GetNextIndex(int currentIndex, int count) => (currentIndex + 1) % count;

    public int GetPreviousIndex(int currentIndex, int count) => (currentIndex - 1 + count) % count;

}

public sealed record SlideshowStartPlan(
    bool CanStart,
    string? ValidationMessage,
    bool ShouldRefreshEndpoints,
    string? ResolvedCommandUrl);

public sealed record SlideshowState(
    bool IsRunning,
    bool IsPaused,
    bool IsScreenOff,
    bool PendingInitialNavigation,
    bool SlideshowLoopStarted,
    int? CurrentIndex,
    string? LastNavigatedUrl,
    string? ScreenOffReturnUrl,
    string StatusMessage);

public sealed record ScreenSwitchTransition(
    bool IsScreenOff,
    bool IsPaused,
    bool WasPausedBeforeScreenOff,
    string? ScreenOffReturnUrl,
    string? NavigateToUrl,
    bool ForceRunning);
