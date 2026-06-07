using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DebtManager.Desktop.Controls;

/// <summary>
/// A reusable empty state control that displays an icon, title, message, and optional action button.
/// </summary>
public partial class EmptyStateControl : UserControl
{
    public EmptyStateControl()
    {
        InitializeComponent();
    }

    #region Dependency Properties

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(EmptyStateControl),
        new PropertyMetadata("No items"));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message),
        typeof(string),
        typeof(EmptyStateControl),
        new PropertyMetadata(string.Empty));

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public static readonly DependencyProperty IconTextProperty = DependencyProperty.Register(
        nameof(IconText),
        typeof(string),
        typeof(EmptyStateControl),
        new PropertyMetadata("\u2139"));

    public string IconText
    {
        get => (string)GetValue(IconTextProperty);
        set => SetValue(IconTextProperty, value);
    }

    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText),
        typeof(string),
        typeof(EmptyStateControl),
        new PropertyMetadata("Get Started"));

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand),
        typeof(ICommand),
        typeof(EmptyStateControl),
        new PropertyMetadata(null));

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    public static readonly DependencyProperty IsActionVisibleProperty = DependencyProperty.Register(
        nameof(IsActionVisible),
        typeof(bool),
        typeof(EmptyStateControl),
        new PropertyMetadata(true));

    public bool IsActionVisible
    {
        get => (bool)GetValue(IsActionVisibleProperty);
        set => SetValue(IsActionVisibleProperty, value);
    }

    #endregion
}
