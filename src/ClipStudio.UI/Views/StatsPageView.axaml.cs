using Avalonia.Controls;
using ClipStudio.UI.ViewModels;

namespace ClipStudio.UI.Views;

/// <summary>
/// Code-behind for the Statistics dashboard page.
/// Triggers a data reload whenever the view is attached to the visual tree.
/// </summary>
public partial class StatsPageView : UserControl
{
    /// <summary>Initializes a new instance of <see cref="StatsPageView"/>.</summary>
    public StatsPageView()
    {
        InitializeComponent();
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is StatsPageViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
