using DebtManager.Desktop.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace DebtManager.Desktop.ViewModels;

/// <summary>
/// ViewModel for the toast notification host.
/// Manages a queue of toast messages with auto-dismiss functionality.
/// </summary>
public sealed class ToastHostViewModel : ObservableObject
{
    private readonly IToastService _toastService;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<Guid, DispatcherTimer> _timers = new();
    private readonly object _lock = new();

    /// <summary>
    /// Default duration before a toast auto-dismisses.
    /// </summary>
    public TimeSpan AutoDismissDuration { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Maximum number of toasts to show at once.
    /// </summary>
    public int MaxVisibleToasts { get; set; } = 5;

    public ToastHostViewModel(IToastService toastService)
    {
        _toastService = toastService;
        _dispatcher = Dispatcher.CurrentDispatcher;

        DismissCommand = new RelayCommand<ToastMessage>(Dismiss);

        // Subscribe to toast events
        _toastService.OnToast += OnToastReceived;
    }

    /// <summary>
    /// Collection of currently visible toasts.
    /// </summary>
    public ObservableCollection<ToastMessage> Items { get; } = new();

    /// <summary>
    /// Command to manually dismiss a toast.
    /// </summary>
    public ICommand DismissCommand { get; }

    private void OnToastReceived(ToastMessage toast)
    {
        // Ensure we're on the UI thread
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnToastReceived(toast));
            return;
        }

        lock (_lock)
        {
            // Add the toast
            Items.Insert(0, toast);

            // Trim if we have too many
            while (Items.Count > MaxVisibleToasts)
            {
                var oldest = Items[^1];
                RemoveToast(oldest.Id);
            }

            // Start auto-dismiss timer
            StartDismissTimer(toast);
        }
    }

    private void StartDismissTimer(ToastMessage toast)
    {
        var timer = new DispatcherTimer
        {
            Interval = AutoDismissDuration
        };

        timer.Tick += (s, e) =>
        {
            timer.Stop();
            RemoveToast(toast.Id);
        };

        lock (_lock)
        {
            _timers[toast.Id] = timer;
        }

        timer.Start();
    }

    private void Dismiss(ToastMessage? toast)
    {
        if (toast != null)
        {
            RemoveToast(toast.Id);
        }
    }

    private void RemoveToast(Guid id)
    {
        // Ensure we're on the UI thread
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => RemoveToast(id));
            return;
        }

        lock (_lock)
        {
            // Stop and remove timer
            if (_timers.TryGetValue(id, out var timer))
            {
                timer.Stop();
                _timers.Remove(id);
            }

            // Remove toast from collection
            var toast = Items.FirstOrDefault(t => t.Id == id);
            if (toast != null)
            {
                Items.Remove(toast);
            }
        }
    }

    /// <summary>
    /// Clean up resources.
    /// </summary>
    public void Dispose()
    {
        _toastService.OnToast -= OnToastReceived;

        lock (_lock)
        {
            foreach (var timer in _timers.Values)
            {
                timer.Stop();
            }
            _timers.Clear();
        }
    }
}
