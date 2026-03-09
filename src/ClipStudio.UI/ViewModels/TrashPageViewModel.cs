using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the Trash page, which shows clips that have been moved to the application trash.
/// Provides restore, permanent delete, and empty-trash operations.
/// Auto-purges expired items (older than 30 days) on load.
/// Whether expired or emptied items go to the OS Recycle Bin or are permanently deleted is
/// controlled by <see cref="ISettingsService.Current"/>.<c>TrashExpiredSendToRecycleBin</c>.
/// </summary>
public sealed partial class TrashPageViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISettingsService _settings;

    /// <summary>Gets or sets a value indicating whether a background operation is running.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Gets or sets a status or error message shown in the toolbar.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Gets the collection of trashed clip rows currently displayed.</summary>
    public ObservableCollection<TrashedClipRowViewModel> Items { get; } = new();

    /// <summary>Gets the command that loads trashed clips from the database.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Gets the command that permanently deletes all items in the trash.</summary>
    public IAsyncRelayCommand EmptyTrashCommand { get; }

    /// <summary>Initialises a new <see cref="TrashPageViewModel"/>.</summary>
    /// <param name="scopeFactory">
    /// Factory used to create an isolated DI scope — and therefore an isolated
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> — for every database operation.
    /// This prevents concurrent root-scope DbContext access when multiple page VMs load at the
    /// same time (e.g. the Library reloading while the user navigates to Trash).
    /// </param>
    /// <param name="settings">The settings service used to read user preferences.</param>
    public TrashPageViewModel(IServiceScopeFactory scopeFactory, ISettingsService settings)
    {
        _scopeFactory     = scopeFactory;
        _settings         = settings;
        LoadCommand       = new AsyncRelayCommand(LoadAsync);
        EmptyTrashCommand = new AsyncRelayCommand(EmptyTrashAsync);
    }

    /// <summary>
    /// Purges expired trash entries and loads the remaining trashed clips for display.
    /// </summary>
    /// <summary>
    /// Purges expired trash entries and loads the remaining trashed clips for display.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading     = true;
        StatusMessage = null;
        try
        {
            using var scope      = _scopeFactory.CreateScope();
            var clipService      = scope.ServiceProvider.GetRequiredService<IClipService>();

            // Silently auto-purge items older than 30 days using the configured deletion mode.
            var sendToRecycleBin = _settings.Current.TrashExpiredSendToRecycleBin;
            await clipService.PurgeExpiredTrashAsync(sendToRecycleBin: sendToRecycleBin);

            var trashed = await clipService.GetTrashedAsync();
            Items.Clear();
            foreach (var clip in trashed)
            {
                var clipId = clip.Id;
                Items.Add(new TrashedClipRowViewModel(
                    clip,
                    onRestore:           () => RestoreClipAsync(clipId),
                    onDeletePermanently: () => PermanentlyDeleteClipAsync(clipId),
                    onDeleteForever:     () => TrueDeleteClipAsync(clipId)));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load error: {ex.GetType().Name}: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RestoreClipAsync(int clipId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var clipService = scope.ServiceProvider.GetRequiredService<IClipService>();
            await clipService.RestoreFromTrashAsync(clipId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClipStudio] RestoreFromTrashAsync failed: {ex.Message}");
        }

        await LoadAsync();
    }

    private async Task PermanentlyDeleteClipAsync(int clipId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var clipService = scope.ServiceProvider.GetRequiredService<IClipService>();
            await clipService.PermanentlyDeleteAsync(clipId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClipStudio] PermanentlyDeleteAsync failed: {ex.Message}");
        }

        await LoadAsync();
    }

    private async Task TrueDeleteClipAsync(int clipId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var clipService = scope.ServiceProvider.GetRequiredService<IClipService>();
            await clipService.TrueDeleteAsync(clipId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClipStudio] TrueDeleteAsync failed: {ex.Message}");
        }

        await LoadAsync();
    }

    private async Task EmptyTrashAsync()
    {
        IsLoading = true;
        try
        {
            using var scope      = _scopeFactory.CreateScope();
            var clipService      = scope.ServiceProvider.GetRequiredService<IClipService>();
            var sendToRecycleBin = _settings.Current.TrashExpiredSendToRecycleBin;

            var trashed = await clipService.GetTrashedAsync();
            foreach (var clip in trashed)
            {
                if (sendToRecycleBin)
                    await clipService.PermanentlyDeleteAsync(clip.Id);
                else
                    await clipService.TrueDeleteAsync(clip.Id);
            }

            Items.Clear();
        }
        finally
        {
            IsLoading = false;
        }
    }
}
