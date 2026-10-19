// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace Velora.App;

public partial class MainWindow
{
    private UpdateInfo? _pendingUpdate;
    private bool _updating;

    internal UpdateInfo? PendingUpdate => _pendingUpdate;

    /// <summary>
    /// Busca una versión nueva en GitHub. Sin <paramref name="manual"/> es la comprobación automática (como mucho una vez al día,
    /// en silencio y solo si el usuario no la ha desactivado); con ella responde siempre, con aviso o sin él.
    /// </summary>
    internal async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updating) return;
        if (!manual && (!_settings.AutoUpdateCheck || (DateTime.Now - _settings.LastUpdateCheck).TotalHours < 24)) return;
        try
        {
            if (manual) ShowInfo("Buscando actualizaciones…");
            var info = await UpdateService.CheckAsync();
            _settings.LastUpdateCheck = DateTime.Now;
            _settings.Save();
            if (info == null)
            {
                if (manual) Message($"Velora está al día: tienes la versión {UpdateService.CurrentVersion}, que es la última.");
                return;
            }

            if (!manual && info.Version.ToString() == _settings.SkippedVersion) return;
            _pendingUpdate = info;
            string first = info.Notes.Split('\n').Select(l => l.Trim().TrimStart('#', '-', '*', ' ')).FirstOrDefault(l => l.Length > 0) ?? "";
            UpdateText.Text = $"Hay una versión nueva de Velora: {info.Version} (tienes la {UpdateService.CurrentVersion}). {first}";
            UpdateBar.Visibility = Visibility.Visible;
            if (manual)
            {
                string notes = info.Notes.Length > 700 ? info.Notes[..700] + "…" : info.Notes;
                int r = Ask($"Hay una versión nueva de Velora: {info.Version} (tienes la {UpdateService.CurrentVersion}).\n\n{notes}".Trim(), ["Actualizar ahora", "Más tarde"], 0, 1);
                if (r == 0) await ApplyUpdateAsync(info);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            ErrorLog.Write(ex);
            if (manual) Error("No se pudo comprobar si hay actualizaciones. Revisa la conexión a internet e inténtalo de nuevo.\n\n" + ex.Message);
        }
    }

    private void Message(string text)
    {
        if (IsSilent)
        {
            ShowInfo(text);
            return;
        }

        MessageDialog.Show(this, text, MessageDialog.Kind.Info, ["Aceptar"], 0, 0);
    }

    private async void OnUpdateNow(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is { } info) await SafeRunAsync(() => ApplyUpdateAsync(info));
    }

    private void OnUpdateLater(object sender, RoutedEventArgs e) => UpdateBar.Visibility = Visibility.Collapsed;

    private void OnUpdateSkip(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate != null) _settings.SkippedVersion = _pendingUpdate.Version.ToString();
        _settings.Save();
        UpdateBar.Visibility = Visibility.Collapsed;
    }

    /// <summary>Descarga, comprueba e instala la versión indicada y reinicia Velora.</summary>
    internal async Task ApplyUpdateAsync(UpdateInfo info)
    {
        if (_updating) return;
        if (!UpdateService.CanInstallInPlace)
        {
            // Copia portátil: no se sabe dónde reemplazarla; se abre la página de descargas.
            if (Ask($"Esta copia de Velora no está instalada con Instalar.ps1, así que no puede actualizarse sola.\n\n¿Abrir la página de la versión {info.Version} para descargarla?", ["Abrir la página", "Cancelar"], 0, 1) == 0)
                Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true });
            return;
        }

        int others = Process.GetProcessesByName("Velora").Count(p => p.Id != Environment.ProcessId);
        if (others > 0)
        {
            Error("Hay otras ventanas de Velora abiertas. Ciérralas (guardando antes tus anotaciones) y vuelve a pulsar «Actualizar ahora».");
            return;
        }

        if (!await ConfirmDiscardAsync()) return;
        _updating = true;
        try
        {
            ShowLoading($"Descargando Velora {info.Version}… 0 %");
            var progress = new Progress<double>(p => LoadingText.Text = $"Descargando Velora {info.Version}… {p:P0}");
            string dir = await UpdateService.PrepareAsync(info, progress);
            LoadingText.Text = $"Instalando Velora {info.Version}…";
            string? reopen = _doc?.Path;
            SaveViewState();
            _settings.Save();
            UpdateService.LaunchInstaller(dir, reopen);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _updating = false;
            HideLoading();
            ErrorLog.Write(ex);
            Error("No se pudo actualizar Velora; se sigue usando la versión actual.\n\n" + ex.Message);
        }
    }

    internal void ShowUpdated(string version) => ShowInfo($"Velora se actualizó a la versión {version}.");
}
