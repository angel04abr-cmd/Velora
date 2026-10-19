// SPDX-License-Identifier: GPL-3.0-or-later
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Velora.Pdf;

namespace Velora.App;

public partial class MainWindow
{
    private CancellationTokenSource? _searchCts;
    private readonly List<SearchHit> _hits = new();
    private int _hitIndex = -1;
    private DispatcherTimer? _searchDebounce;
    private bool _searchDone = true;

    private void ShowSearch()
    {
        if (_doc == null) return;
        SearchBar.Visibility = Visibility.Visible;
        FindToggle.IsChecked = true;
        string sel = View.HasSelection ? View.SelectedTextIfCached() ?? "" : "";
        sel = sel.Replace("\r", " ").Replace("\n", " ").Trim();
        if (sel.Length is > 0 and < 120) SearchBox.Text = sel;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void CloseSearch()
    {
        _searchCts?.Cancel();
        _searchDebounce?.Stop();
        SearchBar.Visibility = Visibility.Collapsed;
        FindToggle.IsChecked = false;
        _hits.Clear();
        _hitIndex = -1;
        View.SetSearchResults(null, null);
        View.Focus();
    }

    private void OnFindToggle(object sender, RoutedEventArgs e)
    {
        if (FindToggle.IsChecked == true) ShowSearch();
        else CloseSearch();
    }

    private void OnCloseSearch(object sender, RoutedEventArgs e) => CloseSearch();

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _searchDebounce.Tick -= OnSearchDebounce;
        _searchDebounce.Tick += OnSearchDebounce;
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private void OnSearchDebounce(object? sender, EventArgs e)
    {
        _searchDebounce?.Stop();
        StartSearch();
    }

    private void OnSearchOptionChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) StartSearch();
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (_searchDebounce is { IsEnabled: true })
        {
            _searchDebounce.Stop();
            StartSearch();
            return;
        }

        StepSearch((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : +1);
    }

    internal void StartSearch()
    {
        _searchCts?.Cancel();
        _hits.Clear();
        _hitIndex = -1;
        View.SetSearchResults(null, null);
        string query = SearchBox.Text;
        if (_doc == null || query.Length == 0)
        {
            SearchStatus.Text = "";
            _searchDone = true;
            return;
        }

        var cts = _searchCts = new CancellationTokenSource();
        _searchDone = false;
        _ = RunSearchAsync(_doc, query, MatchCaseCheck.IsChecked == true, WholeWordCheck.IsChecked == true, cts);
    }

    private async Task RunSearchAsync(PdfDocument doc, string query, bool matchCase, bool wholeWord, CancellationTokenSource cts)
    {
        try
        {
            int n = doc.PageCount, start = View.CurrentPage;
            for (int i = 0; i < n; i++)
            {
                int p = (start + i) % n;
                var found = await doc.SearchPageAsync(p, query, matchCase, wholeWord, PdfPriority.Fondo, cts.Token, i);
                if (cts.IsCancellationRequested || doc != _doc) return;
                if (found.Length > 0)
                {
                    var current = _hitIndex >= 0 ? _hits[_hitIndex] : null;
                    _hits.AddRange(found);
                    _hits.Sort((a, b) => a.Page != b.Page ? a.Page.CompareTo(b.Page) : a.Start.CompareTo(b.Start));
                    if (current == null)
                    {
                        // El primer resultado que aparece es el más cercano a donde se estaba leyendo.
                        _hitIndex = _hits.IndexOf(found[0]);
                        ShowCurrentHit();
                    }
                    else
                    {
                        _hitIndex = _hits.IndexOf(current);
                        View.SetSearchResults(_hits, null);
                        View.SetSearchResults(_hits, _hits[_hitIndex]);
                    }
                }

                if (i % 6 == 0 || i == n - 1) UpdateSearchStatus(i + 1, n);
            }

            _searchDone = true;
            UpdateSearchStatus(n, n);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }

    private void UpdateSearchStatus(int pagesDone, int pages)
    {
        if (_hits.Count > 0)
            SearchStatus.Text = string.Format(T.ResultCount, _hitIndex + 1, _hits.Count) + (_searchDone ? "" : "…");
        else
            SearchStatus.Text = _searchDone ? T.NoResults : string.Format(T.Searching, pagesDone, pages);
    }

    private void ShowCurrentHit()
    {
        if (_hitIndex < 0 || _hitIndex >= _hits.Count) return;
        View.SetSearchResults(_hits, _hits[_hitIndex]);
        SearchStatus.Text = string.Format(T.ResultCount, _hitIndex + 1, _hits.Count) + (_searchDone ? "" : "…");
    }

    private void StepSearch(int dir)
    {
        if (SearchBar.Visibility != Visibility.Visible)
        {
            ShowSearch();
            return;
        }

        if (_hits.Count == 0)
        {
            if (SearchBox.Text.Length > 0 && _searchDone) StartSearch();
            return;
        }

        _hitIndex = (_hitIndex + dir + _hits.Count) % _hits.Count;
        ShowCurrentHit();
    }

    // ---- utilidades para las pruebas automáticas ----
    internal int SearchHitCount => _hits.Count;
    internal bool SearchFinished => _searchDone;
}
