// Anonymous visitor count via Azure Application Insights (see docs/網站流量統計.md).
// Only page views are sent: no custom events, no card, deck or save data.
// The build adds this script only to the Azure site, and it does nothing on any other host.
// If the SDK is blocked (ad blockers, offline) the game keeps working.
(function () {
    'use strict';
    var config = {
        host: 'wcggametienxiang0520.z11.web.core.windows.net',
        sdk: 'https://js.monitor.azure.com/scripts/b/ai.3.gbl.min.js',
        connectionString: 'InstrumentationKey=479b216a-9b44-4fe0-ac84-b3eb5f93e0e9;IngestionEndpoint=https://japaneast-1.in.applicationinsights.azure.com/;LiveEndpoint=https://japaneast.livediagnostics.monitor.azure.com/;ApplicationId=487640f9-1a74-49d8-a224-040e37b24507'
    };
    window.wcgAnalyticsConfig = config;
    if (location.hostname !== config.host) return;
    var script = document.createElement('script');
    script.src = config.sdk;
    script.async = true;
    script.crossOrigin = 'anonymous';
    script.onload = function () {
        try {
            var sdk = window.Microsoft && window.Microsoft.ApplicationInsights;
            if (!sdk || !sdk.ApplicationInsights) return;
            var ai = new sdk.ApplicationInsights({ config: {
                connectionString: config.connectionString,
                enableAutoRouteTracking: true,
                disableAjaxTracking: true,
                disableFetchTracking: true,
                disableExceptionTracking: true,
                autoTrackPageVisitTime: false,
                enableUnhandledPromiseRejectionTracking: false,
                disableCookiesUsage: false
            } });
            ai.loadAppInsights();
            ai.trackPageView();
            window.wcgAnalytics = ai;
        } catch (error) { /* analytics must never break the game */ }
    };
    script.onerror = function () { /* blocked or offline: ignore */ };
    document.head.appendChild(script);
})();
