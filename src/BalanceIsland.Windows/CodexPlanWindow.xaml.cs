using System.ComponentModel;
using System.Windows;
using Microsoft.Web.WebView2.Wpf;

namespace BalanceIsland.Windows;

public partial class CodexPlanWindow : Window
{
    public CodexPlanWindow()
    {
        InitializeComponent();
    }

    public WebView2 Browser => PlanBrowser;

    public void NavigateToLogin()
    {
        if (PlanBrowser.CoreWebView2 is null)
        {
            // Initialization happens on first show; retry once the core is ready.
            PlanBrowser.CoreWebView2InitializationCompleted += (_, _) => LoadChatGpt();
            return;
        }
        LoadChatGpt();
    }

    private void LoadChatGpt()
    {
        if (PlanBrowser.CoreWebView2 is null) return;
        PlanBrowser.CoreWebView2.Navigate("https://chatgpt.com/");
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Hiding keeps the signed-in session alive for background refresh;
        // the application disposes the window only on exit or disconnect.
        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
