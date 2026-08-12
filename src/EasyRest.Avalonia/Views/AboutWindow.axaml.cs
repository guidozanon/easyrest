using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using EasyRest.Services;

namespace EasyRest.Avalonia.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        // la versión real la inyecta el CI con -p:Version=<tag> al publicar; en dev queda la del csproj
        VersionText.Text = "v" + UpdateService.CurrentVersion;
    }

    async void Updates_Click(object? sender, RoutedEventArgs e) =>
        await new UpdateWindow().ShowDialog(this);

    async void GitHub_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Launcher is { } launcher)
            await launcher.LaunchUriAsync(new Uri(UpdateService.RepoUrl));
    }
}
