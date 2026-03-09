using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClipStudio.UI.Views;

/// <summary>
/// Simple view model for the crash report dialog — holds the crash text and dump path.
/// </summary>
public sealed class CrashReportDialogViewModel : ObservableObject
{
    /// <summary>Gets or sets the full content of the crash dump file.</summary>
    public string CrashText { get; init; } = string.Empty;

    /// <summary>Gets or sets the absolute path to the crash dump file being displayed.</summary>
    public string DumpFilePath { get; init; } = string.Empty;
}

/// <summary>
/// Modal dialog shown when a crash dump from the previous session is detected.
/// Lets the user send a report via GitHub Issues or dismiss (and delete) the dump.
/// </summary>
public partial class CrashReportDialog : Window
{
    /// <summary>
    /// GitHub Issues URL used when the user clicks "Send Report".
    /// The <c>body</c> parameter is left empty so the user can paste the crash text manually;
    /// URL-encoding the full stack trace causes URLs to exceed browser limits.
    /// Replace <c>Phazertron</c> and <c>REPO</c> with actual values before shipping.
    /// </summary>
    private const string FeedbackUrl = "https://github.com/Phazertron/ClipStudio/issues/new?labels=crash&title=Crash+report";

    /// <summary>
    /// Parameterless constructor required by the Avalonia XAML runtime loader and the visual designer.
    /// </summary>
    public CrashReportDialog() : this(new CrashReportDialogViewModel()) { }

    /// <summary>Initialises the dialog with a crash dump view model.</summary>
    /// <param name="vm">View model containing crash text and file path.</param>
    public CrashReportDialog(CrashReportDialogViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnSendReportClicked(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(FeedbackUrl) { UseShellExecute = true });
        }
        catch
        {
            // Opening a browser is best-effort.
        }

        // Delete the dump after sending so it is not shown again.
        if (DataContext is CrashReportDialogViewModel vm)
            CrashReporter.DeleteCrashDump(vm.DumpFilePath);

        Close();
    }

    private void OnDismissClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CrashReportDialogViewModel vm)
            CrashReporter.DeleteCrashDump(vm.DumpFilePath);

        Close();
    }
}
