using System.Diagnostics;
using System.Threading.Tasks;
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
        if (DataContext is CrashReportDialogViewModel vm)
        {
            var url = GitHubIssueHelper.BuildCrashIssueUrl(vm.CrashText);
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* opening a browser is best-effort */ }

            CrashReporter.DeleteCrashDump(vm.DumpFilePath);
        }

        Close();
    }


    private async void OnCopyClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not CrashReportDialogViewModel vm) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;

        await clipboard.SetTextAsync(vm.CrashText);

        // Briefly change the button label to confirm the copy.
        CopyButton.Content = "Copied!";
        await Task.Delay(1500);
        CopyButton.Content = "Copy";
    }

    private void OnDismissClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CrashReportDialogViewModel vm)
            CrashReporter.DeleteCrashDump(vm.DumpFilePath);

        Close();
    }
}
