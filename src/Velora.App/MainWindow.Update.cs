// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Velora.Updates;

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
            if (!manual) await WaitForIdleAsync();
            await OfferUpdateAsync(info);
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

    /// <summary>Espera a que termine de abrirse el documento: el aviso no debe taparlo mientras carga.</summary>
    private async Task WaitForIdleAsync()
    {
        for (int i = 0; i < 100 && (Loading.Visibility == Visibility.Visible || _opening); i++) await Task.Delay(300);
    }

    /// <summary>Ventana con las novedades de la versión nueva y los botones Actualizar ahora, Más tarde y Omitir esta versión.</summary>
    private async Task OfferUpdateAsync(UpdateInfo info)
    {
        if (IsSilent)
        {
            ShowInfo($"Hay una versión nueva de Velora: {info.Version}.");
            return;
        }

        var dlg = new UpdateDialog(info, UpdateService.CurrentVersion.ToString(), UpdateService.CanInstallInPlace,
            (progress, status) => ApplyUpdateAsync(info, progress, status), this);
        dlg.ShowDialog();
        if (dlg.Result == UpdateDialog.Choice.Skip)
        {
            _settings.SkippedVersion = info.Version.ToString();
            _settings.Save();
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Descarga, comprueba e instala la versión indicada y reinicia Velora. Con <paramref name="progress"/> el avance se muestra en la ventana de
    /// actualización; sin él (pruebas automáticas) se usa la capa de «cargando» de la ventana principal.
    /// </summary>
    internal async Task ApplyUpdateAsync(UpdateInfo info, IProgress<double>? progress = null, Action<string>? status = null)
    {
        if (_updating) return;
        if (!UpdateService.CanInstallInPlace)
        {
            // Copia portátil (no instalada con el instalador): no se sabe dónde reemplazarla; se abre la página de descargas.
            if (Ask($"Esta copia de Velora no está instalada con el instalador (.msi), así que no puede actualizarse sola.\n\n¿Abrir la página de la versión {info.Version} para descargarla?", ["Abrir la página", "Cancelar"], 0, 1) == 0)
                Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true });
            return;
        }

        int others = Process.GetProcessesByName("Velora").Count(p => p.Id != Environment.ProcessId);
        if (others > 0) throw new InvalidOperationException("Hay otras ventanas de Velora abiertas. Ciérralas (guardando antes tus anotaciones) y vuelve a pulsar «Actualizar ahora».");

        if (!await ConfirmDiscardAsync()) throw new InvalidOperationException("Actualización cancelada: hay cambios sin guardar.");
        _updating = true;
        bool overlay = progress == null;
        try
        {
            if (overlay) ShowLoading($"Descargando Velora {info.Version}… 0 %");
            status?.Invoke($"Descargando Velora {info.Version}…");
            var report = new Progress<double>(p =>
            {
                progress?.Report(p);
                if (overlay) LoadingText.Text = $"Descargando Velora {info.Version}… {p:P0}";
                else status?.Invoke($"Descargando Velora {info.Version}… {p:P0}");
            });
            string msi = await UpdateService.PrepareAsync(info, report);
            string installing = $"Instalando Velora {info.Version}… Windows pedirá permiso de administrador; Velora se cerrará y se abrirá sola.";
            if (overlay) LoadingText.Text = installing;
            else status?.Invoke(installing);
            string? reopen = _doc?.Path;
            SaveViewState();
            _settings.Save();
            UpdateService.LaunchApply(msi, info, reopen);
            Application.Current.Shutdown();
        }
        catch
        {
            _updating = false;
            if (overlay) HideLoading();
            throw;
        }
    }

    internal void ShowUpdated(string version) => ShowInfo($"Velora se actualizó a la versión {version}.");
}
