// SPDX-License-Identifier: GPL-3.0-or-later
// Idea tomada de Papers/GNOME (libview/pps-job-scheduler.c): todo el trabajo pesado se hace
// fuera de la ventana, en una cola con prioridades y con tareas cancelables. Implementación propia.
using Velora.Pdf.Native;

namespace Velora.Pdf;

/// <summary>Prioridad de una tarea. Los números menores se ejecutan antes.</summary>
public enum PdfPriority
{
    /// <summary>Acciones del usuario (abrir, guardar, anotar, copiar).</summary>
    Interactivo = 0,
    /// <summary>Páginas que se ven ahora mismo.</summary>
    Urgente = 1,
    /// <summary>Miniaturas visibles.</summary>
    Alta = 2,
    /// <summary>Páginas cercanas, para tenerlas listas.</summary>
    Baja = 3,
    /// <summary>Búsquedas y exploraciones largas.</summary>
    Fondo = 4
}

/// <summary>
/// PDFium no es seguro entre hilos: un único hilo dedicado ejecuta todas las llamadas.
/// Las tareas se ordenan por prioridad y, a igual prioridad, por cercanía a lo que se ve.
/// </summary>
public sealed class PdfWorker
{
    private static readonly Lazy<PdfWorker> SharedInstance = new(() => new PdfWorker());
    public static PdfWorker Shared => SharedInstance.Value;

    private readonly PriorityQueue<IJob, (int Priority, int Sub, long Seq)> _queue = new();
    private readonly object _gate = new();
    private readonly Thread _thread;
    private long _seq;

    private PdfWorker()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "Velora-PDFium" };
        _thread.Start();
    }

    /// <summary>Número de tareas en espera (útil para pruebas y diagnóstico).</summary>
    public int Pending
    {
        get { lock (_gate) return _queue.Count; }
    }

    public Task<T> Run<T>(PdfPriority priority, Func<T> work, CancellationToken ct = default, int sub = 0)
    {
        var job = new Job<T>(work, ct);
        if (ct.IsCancellationRequested)
        {
            job.Cancel();
            return job.Task;
        }

        lock (_gate)
        {
            _queue.Enqueue(job, ((int)priority, sub, _seq++));
            Monitor.Pulse(_gate);
        }

        return job.Task;
    }

    public Task Run(PdfPriority priority, Action work, CancellationToken ct = default, int sub = 0) =>
        Run<object?>(priority, () => { work(); return null; }, ct, sub);

    private void Loop()
    {
        Pdfium.FPDF_InitLibrary();
        while (true)
        {
            IJob job;
            lock (_gate)
            {
                while (_queue.Count == 0) Monitor.Wait(_gate);
                job = _queue.Dequeue();
            }

            job.Execute();
        }
    }

    private interface IJob
    {
        void Execute();
    }

    private sealed class Job<T> : IJob
    {
        private readonly Func<T> _work;
        private readonly CancellationToken _ct;
        private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Job(Func<T> work, CancellationToken ct)
        {
            _work = work;
            _ct = ct;
        }

        public Task<T> Task => _tcs.Task;

        public void Cancel() => _tcs.TrySetCanceled(_ct);

        public void Execute()
        {
            if (_ct.IsCancellationRequested)
            {
                Cancel();
                return;
            }

            try
            {
                _tcs.TrySetResult(_work());
            }
            catch (Exception ex)
            {
                _tcs.TrySetException(ex);
            }
        }
    }
}
