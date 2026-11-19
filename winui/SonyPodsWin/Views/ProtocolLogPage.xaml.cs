using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SonyPodsWin.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace SonyPodsWin.Views;

public sealed partial class ProtocolLogPage : Page
{
    private readonly ObservableCollection<ProtocolLogEntry> _filteredLogs = new();
    private AppStateViewModel? _state;

    public ProtocolLogPage()
    {
        InitializeComponent();
        LogListView.ItemsSource = _filteredLogs;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        AppStateViewModel vm = e.Parameter as AppStateViewModel ?? App.State;
        AttachState(vm);
    }

    private void AttachState(AppStateViewModel vm)
    {
        if (ReferenceEquals(_state, vm))
        {
            RebuildFilter();
            return;
        }

        if (_state is not null)
        {
            _state.LogEntries.CollectionChanged -= OnSourceLogsChanged;
        }

        _state = vm;
        _state.LogEntries.CollectionChanged += OnSourceLogsChanged;
        RebuildFilter();
    }

    private void OnSourceLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            ProtocolLogEntry? lastAdded = null;
            foreach (object obj in e.NewItems)
            {
                if (obj is ProtocolLogEntry entry && MatchesFilter(entry))
                {
                    _filteredLogs.Add(entry);
                    lastAdded = entry;
                }
            }

            if (lastAdded is not null)
            {
                LogListView.ScrollIntoView(lastAdded);
            }

            return;
        }

        RebuildFilter();
    }

    private void OnFilterChanged(object sender, SelectionChangedEventArgs e) => RebuildFilter();

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e) => RebuildFilter();

    private void RebuildFilter()
    {
        if (_state is null)
        {
            return;
        }

        _filteredLogs.Clear();
        foreach (ProtocolLogEntry entry in _state.LogEntries)
        {
            if (MatchesFilter(entry))
            {
                _filteredLogs.Add(entry);
            }
        }

        if (_filteredLogs.Count > 0)
        {
            LogListView.ScrollIntoView(_filteredLogs[^1]);
        }
    }

    private bool MatchesFilter(ProtocolLogEntry entry)
    {
        string? levelTag = (LevelFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        bool levelOk = levelTag switch
        {
            "Info" => entry.Level == ProtocolLogLevel.Info,
            "Frame" => entry.Level == ProtocolLogLevel.Frame,
            "Error" => entry.Level == ProtocolLogLevel.Error || entry.Level == ProtocolLogLevel.Warning,
            _ => true,
        };

        if (!levelOk)
        {
            return false;
        }

        string keyword = SearchBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(keyword))
        {
            return true;
        }

        return entry.FormattedLine.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void OnCopyLogsClicked(object sender, RoutedEventArgs e)
    {
        string text = string.Join(Environment.NewLine, _filteredLogs.Select(x => x.FormattedLine));
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void OnClearLogsClicked(object sender, RoutedEventArgs e)
    {
        _state?.ClearLogs();
    }
}
