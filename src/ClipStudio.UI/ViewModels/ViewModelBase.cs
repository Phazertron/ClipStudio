using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Base class for all view models in the ClipStudio UI layer.
/// Provides change-notification infrastructure via <see cref="ObservableObject"/>.
/// </summary>
public abstract class ViewModelBase : ObservableObject
{
}
