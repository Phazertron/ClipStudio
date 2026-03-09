namespace ClipStudio.UI.ViewModels.WizardSteps;

/// <summary>
/// Abstract base class for all wizard step view models.
/// Provides the common contract (title, step number, navigation readiness) that
/// <see cref="SetupWizardViewModel"/> depends on to drive the wizard.
/// </summary>
public abstract class WizardStepViewModel : ViewModelBase
{
    /// <summary>Gets the display title shown in the wizard header for this step.</summary>
    public abstract string Title { get; }

    /// <summary>Gets the 1-based ordinal position of this step within the wizard.</summary>
    public abstract int StepNumber { get; }

    /// <summary>
    /// Gets a value indicating whether the user may proceed to the next step.
    /// Override to add per-step validation. Defaults to <see langword="true"/>.
    /// </summary>
    public virtual bool CanProceed => true;
}
