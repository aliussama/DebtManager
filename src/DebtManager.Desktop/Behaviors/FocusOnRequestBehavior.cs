using DebtManager.Desktop.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace DebtManager.Desktop.Behaviors;

/// <summary>
/// Attached behavior that focuses a control when the IFocusRequestService
/// fires FocusRequested with a matching key. Unsubscribes on Unloaded
/// to avoid memory leaks.
/// </summary>
public static class FocusOnRequestBehavior
{
    // --- FocusKey attached property ---
    public static readonly DependencyProperty FocusKeyProperty =
        DependencyProperty.RegisterAttached(
            "FocusKey",
            typeof(string),
            typeof(FocusOnRequestBehavior),
            new PropertyMetadata(null, OnSettingsChanged));

    public static string? GetFocusKey(DependencyObject obj) => (string?)obj.GetValue(FocusKeyProperty);
    public static void SetFocusKey(DependencyObject obj, string? value) => obj.SetValue(FocusKeyProperty, value);

    // --- FocusService attached property ---
    public static readonly DependencyProperty FocusServiceProperty =
        DependencyProperty.RegisterAttached(
            "FocusService",
            typeof(IFocusRequestService),
            typeof(FocusOnRequestBehavior),
            new PropertyMetadata(null, OnSettingsChanged));

    public static IFocusRequestService? GetFocusService(DependencyObject obj) => (IFocusRequestService?)obj.GetValue(FocusServiceProperty);
    public static void SetFocusService(DependencyObject obj, IFocusRequestService? value) => obj.SetValue(FocusServiceProperty, value);

    // --- Internal: store the handler so we can unsubscribe ---
    private static readonly DependencyProperty HandlerProperty =
        DependencyProperty.RegisterAttached(
            "__FocusHandler",
            typeof(EventHandler<FocusRequestEventArgs>),
            typeof(FocusOnRequestBehavior),
            new PropertyMetadata(null));

    private static void OnSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        // Detach any previous handler
        Detach(element);

        var key = GetFocusKey(element);
        var service = GetFocusService(element);

        if (string.IsNullOrEmpty(key) || service == null)
            return;

        // Create and attach handler
        EventHandler<FocusRequestEventArgs> handler = (_, args) =>
        {
            if (!string.Equals(args.TargetKey, GetFocusKey(element), StringComparison.Ordinal))
                return;

            element.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                element.Focus();
                if (element is TextBoxBase textBox)
                {
                    textBox.SelectAll();
                }
            });
        };

        element.SetValue(HandlerProperty, handler);
        service.FocusRequested += handler;

        // Unsubscribe on unload to prevent memory leaks
        element.Unloaded += OnElementUnloaded;
    }

    private static void OnElementUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            element.Unloaded -= OnElementUnloaded;
            Detach(element);
        }
    }

    private static void Detach(FrameworkElement element)
    {
        var handler = (EventHandler<FocusRequestEventArgs>?)element.GetValue(HandlerProperty);
        var service = GetFocusService(element);

        if (handler != null && service != null)
        {
            service.FocusRequested -= handler;
        }

        element.ClearValue(HandlerProperty);
    }
}
