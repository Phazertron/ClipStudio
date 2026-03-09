using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Represents a single step indicator dot shown in the wizard navigation header.
/// Tracks whether the step is the active step or has already been completed.
/// </summary>
public sealed partial class StepIndicatorViewModel : ViewModelBase
{
    /// <summary>Gets the 1-based step number displayed inside the indicator.</summary>
    public int Number { get; }

    /// <summary>Gets or sets whether this step is the one currently displayed.</summary>
    [ObservableProperty]
    private bool _isCurrent;

    /// <summary>Gets or sets whether this step has been passed and is fully complete.</summary>
    [ObservableProperty]
    private bool _isCompleted;

    /// <summary>
    /// Initialises a new <see cref="StepIndicatorViewModel"/>.
    /// </summary>
    /// <param name="number">1-based step number.</param>
    public StepIndicatorViewModel(int number)
    {
        Number = number;
    }
}
