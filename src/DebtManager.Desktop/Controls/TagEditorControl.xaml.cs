using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DebtManager.Desktop.Controls;

public partial class TagEditorControl : UserControl
{
    public TagEditorControl()
    {
        InitializeComponent();
    }

    #region Dependency Properties

    public static readonly DependencyProperty TagsProperty = DependencyProperty.Register(
        nameof(Tags),
        typeof(ObservableCollection<string>),
        typeof(TagEditorControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public ObservableCollection<string>? Tags
    {
        get => (ObservableCollection<string>?)GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    public static readonly DependencyProperty NewTagTextProperty = DependencyProperty.Register(
        nameof(NewTagText),
        typeof(string),
        typeof(TagEditorControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string NewTagText
    {
        get => (string)GetValue(NewTagTextProperty);
        set => SetValue(NewTagTextProperty, value);
    }

    public static readonly DependencyProperty AddTagCommandProperty = DependencyProperty.Register(
        nameof(AddTagCommand),
        typeof(ICommand),
        typeof(TagEditorControl),
        new PropertyMetadata(null));

    public ICommand? AddTagCommand
    {
        get => (ICommand?)GetValue(AddTagCommandProperty);
        set => SetValue(AddTagCommandProperty, value);
    }

    public static readonly DependencyProperty RemoveTagCommandProperty = DependencyProperty.Register(
        nameof(RemoveTagCommand),
        typeof(ICommand),
        typeof(TagEditorControl),
        new PropertyMetadata(null));

    public ICommand? RemoveTagCommand
    {
        get => (ICommand?)GetValue(RemoveTagCommandProperty);
        set => SetValue(RemoveTagCommandProperty, value);
    }

    public static readonly DependencyProperty TagsChangedCommandProperty = DependencyProperty.Register(
        nameof(TagsChangedCommand),
        typeof(ICommand),
        typeof(TagEditorControl),
        new PropertyMetadata(null));

    public ICommand? TagsChangedCommand
    {
        get => (ICommand?)GetValue(TagsChangedCommandProperty);
        set => SetValue(TagsChangedCommandProperty, value);
    }

    #endregion
}
