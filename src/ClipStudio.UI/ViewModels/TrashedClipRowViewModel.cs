using System;
using System.Threading.Tasks;
using ClipStudio.Core.Entities;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// Read-only view model projection of a single trashed <see cref="Clip"/> row
/// displayed on the Trash page.
/// Commands are backed by delegate closures to avoid cross-template parent references.
/// </summary>
public sealed class TrashedClipRowViewModel : ViewModelBase
{
    private const int RetentionDays = 30;

    /// <summary>Gets the unique database identifier of the clip.</summary>
    public int ClipId { get; }

    /// <summary>Gets the file name of the trashed clip.</summary>
    public string FileName { get; }

    /// <summary>Gets the original file path of the clip before it was trashed.</summary>
    public string OriginalPath { get; }

    /// <summary>Gets the UTC date and time when this clip was moved to the trash.</summary>
    public DateTime TrashedAt { get; }

    /// <summary>Gets the human-readable trashed-at date string.</summary>
    public string TrashedAtDisplay => TrashedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    /// <summary>Gets the number of days remaining before this clip is auto-purged.</summary>
    public int DaysRemaining { get; }

    /// <summary>Gets a display string for the days remaining until auto-purge.</summary>
    public string DaysRemainingDisplay =>
        DaysRemaining > 0 ? $"{DaysRemaining}d remaining" : "Purge pending";

    /// <summary>
    /// Gets a value indicating whether this clip's file was missing when it was trashed
    /// (i.e. it has no trash path and cannot be restored to its original location).
    /// </summary>
    public bool IsBroken { get; }

    /// <summary>Gets the command that restores this clip from the trash to the library.</summary>
    public IAsyncRelayCommand RestoreCommand { get; }

    /// <summary>
    /// Gets the command that opens the Relocate dialog for this broken-trashed clip.
    /// Only relevant when <see cref="IsBroken"/> is <see langword="true"/>.
    /// </summary>
    public IRelayCommand RelocateCommand { get; }

    /// <summary>
    /// Gets the command that moves this clip's file to the operating-system recycle bin
    /// and removes the database record.
    /// </summary>
    public IAsyncRelayCommand DeletePermanentlyCommand { get; }

    /// <summary>
    /// Gets the command that irrecoverably deletes this clip's file (bypasses the system recycle bin)
    /// and removes the database record. This action cannot be undone.
    /// </summary>
    public IAsyncRelayCommand DeleteForeverCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="TrashedClipRowViewModel"/> from a <see cref="Clip"/> entity.
    /// </summary>
    /// <param name="clip">The trashed clip entity to project.</param>
    /// <param name="onRestore">Async action invoked when <see cref="RestoreCommand"/> is executed.</param>
    /// <param name="onRelocate">Action invoked when <see cref="RelocateCommand"/> is executed (view opens dialog).</param>
    /// <param name="onDeletePermanently">Async action invoked when <see cref="DeletePermanentlyCommand"/> is executed.</param>
    /// <param name="onDeleteForever">Async action invoked when <see cref="DeleteForeverCommand"/> is executed.</param>
    public TrashedClipRowViewModel(
        Clip clip,
        Func<Task> onRestore,
        Action onRelocate,
        Func<Task> onDeletePermanently,
        Func<Task> onDeleteForever)
    {
        ClipId       = clip.Id;
        FileName     = clip.FileName;
        OriginalPath = clip.FilePath;
        TrashedAt    = clip.DeletedAt ?? DateTime.UtcNow;
        IsBroken     = string.IsNullOrEmpty(clip.TrashPath);

        var elapsed    = (DateTime.UtcNow - TrashedAt).TotalDays;
        DaysRemaining  = Math.Max(0, RetentionDays - (int)Math.Floor(elapsed));

        RestoreCommand           = new AsyncRelayCommand(onRestore);
        RelocateCommand          = new RelayCommand(onRelocate);
        DeletePermanentlyCommand = new AsyncRelayCommand(onDeletePermanently);
        DeleteForeverCommand     = new AsyncRelayCommand(onDeleteForever);
    }
}
