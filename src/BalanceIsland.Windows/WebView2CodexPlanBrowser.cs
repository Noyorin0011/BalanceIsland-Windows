using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BalanceIsland.Windows;

public sealed class WebView2CodexPlanBrowser : ICodexPlanBrowser, IDisposable
{
    private const string ProfileRelativePath = @"BalanceIslandWebView2ChatGPTPlan";
    private const string ReadUsageScript = """
        (async () => {
          try {
            const session = await fetch('/api/auth/session', { credentials: 'include' });
            const usage = await fetch('/backend-api/wham/usage', { credentials: 'include' });
            const rawUsage = await usage.json().catch(() => ({}));
            const rawRateLimit = rawUsage && rawUsage.rate_limit ? rawUsage.rate_limit : rawUsage;
            const sanitizeWindow = (w) => {
              if (!w) return null;
              const out = {};
              if (typeof w.used_percent === 'number') out.used_percent = w.used_percent;
              if (typeof w.reset_at === 'number') out.reset_at = w.reset_at;
              if (typeof w.limit_window_seconds === 'number') out.limit_window_seconds = w.limit_window_seconds;
              return out;
            };
            return {
              status: usage.status,
              body: {
                plan_type: rawUsage && typeof rawUsage.plan_type === 'string' ? rawUsage.plan_type : null,
                rate_limit: {
                  primary_window: sanitizeWindow(rawRateLimit && rawRateLimit.primary_window),
                  secondary_window: sanitizeWindow(rawRateLimit && rawRateLimit.secondary_window)
                }
              }
            };
          } catch (error) {
            return { status: 0, body: {} };
          }
        })()
        """;

    private readonly string _userDataFolder;
    private WebView2? _webView;
    private bool _initialized;
    private bool _disposed;

    public WebView2CodexPlanBrowser()
    {
        _userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProfileRelativePath);
    }

    public bool IsOnTrustedUsageOrigin
    {
        get
        {
            if (_webView is null || _webView.CoreWebView2 is null || _webView.Source is null)
                return false;
            return CodexPlanOriginPolicy.CanRead(_webView.Source);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_initialized) return;
        var environment = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
        cancellationToken.ThrowIfCancellationRequested();
        _webView = new WebView2();
        await _webView.EnsureCoreWebView2Async(environment);
        ConfigureSettings();
        WireSecurityEvents();
        _initialized = true;
    }

    public async Task<CodexPlanBrowserResult> ReadFilteredUsageAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!IsOnTrustedUsageOrigin)
            return new CodexPlanBrowserResult(0, "{}");

        var scriptResult = await _webView!.CoreWebView2!.ExecuteScriptAsync(ReadUsageScript);
        var envelope = CodexPlanScriptEnvelopeParser.Parse(scriptResult);
        return new CodexPlanBrowserResult(envelope.StatusCode, envelope.BodyJson);
    }

    public async Task ClearProfileAsync(CancellationToken cancellationToken)
    {
        if (_webView?.CoreWebView2 is { } core)
        {
            try
            {
                await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
            }
            catch (Exception)
            {
                // Profile cleanup is retried at next initialization; failure is not fatal.
            }
            try
            {
                core.NavigateToString("about:blank");
            }
            catch (Exception)
            {
            }
        }
        _webView?.Dispose();
        _webView = null;

        try
        {
            if (Directory.Exists(_userDataFolder))
                Directory.Delete(_userDataFolder, recursive: true);
        }
        catch (Exception)
        {
            // Leave profile cleanup pending for next launch.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            await ClearProfileAsync(CancellationToken.None);
        }
        catch (Exception)
        {
        }
        _webView?.Dispose();
    }

    public void Dispose()
    {
        _disposed = true;
        _webView?.Dispose();
    }

    private void ConfigureSettings()
    {
        var settings = _webView!.CoreWebView2!.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsStatusBarEnabled = false;
    }

    private void WireSecurityEvents()
    {
        var core = _webView!.CoreWebView2!;
        core.NavigationStarting += OnNavigationStarting;
        core.PermissionRequested += OnPermissionRequested;
        core.DownloadStarting += OnDownloadStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.ProcessFailed += OnProcessFailed;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return;
        e.Cancel = true;
    }

    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = CoreWebView2PermissionState.Deny;
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        e.Cancel = true;
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception)
            {
            }
        }
        e.Handled = true;
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        // Do not auto-rebuild repeatedly; the caller observes Runtime through a failed read.
        _initialized = false;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WebView2CodexPlanBrowser));
    }
}
