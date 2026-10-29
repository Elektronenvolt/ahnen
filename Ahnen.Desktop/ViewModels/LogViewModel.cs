using System.Windows.Threading;
using Ahnen.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Ahnen.Desktop.ViewModels;

/// <summary>Collapsible live view of the shared in-memory log buffer (same capacity as the web viewer).</summary>
public sealed partial class LogViewModel(ApplicationLogBuffer buffer) : ObservableObject
{
    private DispatcherTimer? _timer;
    private long _version = -1;

    [ObservableProperty]
    private IReadOnlyList<ApplicationLogEntry> _entries = buffer.Snapshot();

    [ObservableProperty]
    private bool _isExpanded;

    public bool IsEmpty => Entries.Count == 0;

    partial void OnEntriesChanged(IReadOnlyList<ApplicationLogEntry> value) => OnPropertyChanged(nameof(IsEmpty));

    partial void OnIsExpandedChanged(bool value)
    {
        if (!value)
        {
            _timer?.Stop();
            return;
        }
        Refresh();
        _timer ??= CreateTimer();
        _timer.Start();
    }

    /// <summary>Reloads the entries when the buffer changed since the last refresh.</summary>
    public void Refresh()
    {
        if (buffer.Version == _version) return;
        _version = buffer.Version;
        Entries = buffer.Snapshot();
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Refresh();
        return timer;
    }
}
