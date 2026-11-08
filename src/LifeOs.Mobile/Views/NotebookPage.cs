using LifeOs.Api.Components;
using LifeOs.Api.Ui;
using Microsoft.AspNetCore.Components.WebView.Maui;

namespace LifeOs.Mobile.Views;

public sealed class NotebookPage : ContentPage
{
    private readonly BlazorWebView webView;

    public NotebookPage()
    {
        Title = "Notatki";
        SafeAreaEdges = SafeAreaEdges.All;
        BackgroundColor = Color.FromArgb("F6F7F2");
        webView = new BlazorWebView { HostPage = "wwwroot/index.html" };
        webView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Routes) });
        Content = webView;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Return from the recorder refreshes the list without discarding a text draft.
        try { await webView.TryDispatchAsync(sp => sp.GetRequiredService<CaptureChanges>().Notify()); }
        catch (ObjectDisposedException) { }
    }
}
