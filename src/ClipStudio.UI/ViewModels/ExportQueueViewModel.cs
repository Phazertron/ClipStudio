using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the Export Queue page.
/// Displays pending and completed export jobs in separate sections.
/// Provides commands to process the pending queue and cancel individual pending jobs.
/// Each pending row carries its own cancel command backed by a delegate closure.
/// </summary>
public sealed partial class ExportQueueViewModel : ViewModelBase
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Gets the collection of jobs that are pending or currently processing.</summary>
    public ObservableCollection<ExportJobRowViewModel> PendingJobs { get; } = new();

    /// <summary>Gets the collection of jobs that have completed, failed, or been cancelled.</summary>
    public ObservableCollection<ExportJobRowViewModel> CompletedJobs { get; } = new();

    /// <summary>Gets or sets a value indicating whether a background operation is running.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Gets or sets a value indicating whether the queue is currently being processed.</summary>
    [ObservableProperty]
    private bool _isProcessing;

    /// <summary>Gets or sets a status message shown after processing or cancelling.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Gets the number of pending jobs.</summary>
    [ObservableProperty]
    private int _pendingCount;

    /// <summary>Gets the command that refreshes the job list from the database.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Gets the command that processes all pending jobs sequentially.</summary>
    public IAsyncRelayCommand ProcessQueueCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="ExportQueueViewModel"/>.
    /// </summary>
    /// <param name="scopeFactory">
    /// Factory used to create an isolated DI scope — and therefore an isolated
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> — for every database operation.
    /// This prevents concurrent root-scope DbContext access when multiple page VMs load at the
    /// same time (e.g. the Library reloading in the background while the user navigates here).
    /// </param>
    public ExportQueueViewModel(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory       = scopeFactory;
        LoadCommand         = new AsyncRelayCommand(LoadAsync);
        ProcessQueueCommand = new AsyncRelayCommand(ProcessQueueAsync);
    }

    /// <summary>
    /// Refreshes the job list from the export queue, splitting jobs into pending and completed sections.
    /// Each pending row receives a delegate closure so it can cancel itself without referencing the parent VM.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading     = true;
        StatusMessage = null;

        try
        {
            using var scope   = _scopeFactory.CreateScope();
            var exportService = scope.ServiceProvider.GetRequiredService<IExportService>();

            var all = await exportService.GetAllAsync();
            PendingJobs.Clear();
            CompletedJobs.Clear();

            int pending = 0;
            foreach (var job in all)
            {
                if (job.Status is ExportJobStatus.Pending or ExportJobStatus.Processing)
                {
                    var capturedId = job.Id;
                    var row = new ExportJobRowViewModel(job, () => CancelJobAsync(capturedId));
                    PendingJobs.Add(row);
                    pending++;
                }
                else
                {
                    CompletedJobs.Add(new ExportJobRowViewModel(job));
                }
            }

            PendingCount = pending;
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

    private async Task ProcessQueueAsync()
    {
        IsProcessing  = true;
        StatusMessage = null;

        try
        {
            using var scope   = _scopeFactory.CreateScope();
            var exportService = scope.ServiceProvider.GetRequiredService<IExportService>();

            await exportService.ProcessQueueAsync();
            StatusMessage = "Queue processed.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task CancelJobAsync(int jobId)
    {
        try
        {
            using var scope   = _scopeFactory.CreateScope();
            var exportService = scope.ServiceProvider.GetRequiredService<IExportService>();

            await exportService.CancelJobAsync(jobId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cancel failed: {ex.Message}";
        }
    }
}
