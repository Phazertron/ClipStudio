using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Represents a single row in the filter-preset list, exposing the preset name and
/// commands to load or delete it.
/// </summary>
public sealed class FilterPresetRowViewModel : ViewModelBase
{
    /// <summary>Gets the database identifier of the preset.</summary>
    public int Id { get; }

    /// <summary>Gets the display name of the preset.</summary>
    public string Name { get; }

    /// <summary>Gets the command that applies this preset to the library filter.</summary>
    public IRelayCommand LoadCommand { get; }

    /// <summary>Gets the command that permanently deletes this preset.</summary>
    public IAsyncRelayCommand DeleteCommand { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="FilterPresetRowViewModel"/>.
    /// </summary>
    /// <param name="id">Preset identifier.</param>
    /// <param name="name">Preset display name.</param>
    /// <param name="onLoad">Action invoked when the user clicks Load.</param>
    /// <param name="onDeleteAsync">Async action invoked when the user clicks Delete.</param>
    public FilterPresetRowViewModel(int id, string name, Action onLoad, Func<Task> onDeleteAsync)
    {
        Id = id;
        Name = name;
        LoadCommand = new RelayCommand(onLoad);
        DeleteCommand = new AsyncRelayCommand(onDeleteAsync);
    }
}
