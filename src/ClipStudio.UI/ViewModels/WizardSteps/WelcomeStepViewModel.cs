namespace ClipStudio.UI.ViewModels.WizardSteps;

/// <summary>
/// View model for the first wizard step: a welcome and feature overview screen.
/// No user input is required; the step is always ready to proceed.
/// </summary>
public sealed class WelcomeStepViewModel : WizardStepViewModel
{
    /// <inheritdoc/>
    public override string Title => "Welcome to ClipStudio";

    /// <inheritdoc/>
    public override int StepNumber => 1;
}
