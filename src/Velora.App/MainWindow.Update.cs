// SPDX-License-Identifier: GPL-3.0-or-later
using System.Windows;

namespace Velora.App;

public partial class MainWindow
{
    private Task CheckForUpdatesAsync(bool manual) => Task.CompletedTask;

    private void OnUpdateNow(object sender, RoutedEventArgs e) { }

    private void OnUpdateLater(object sender, RoutedEventArgs e) => UpdateBar.Visibility = Visibility.Collapsed;

    private void OnUpdateSkip(object sender, RoutedEventArgs e) => UpdateBar.Visibility = Visibility.Collapsed;
}
