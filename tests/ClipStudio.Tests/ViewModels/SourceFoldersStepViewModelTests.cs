using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using ClipStudio.Core.Interfaces;
using ClipStudio.UI.ViewModels.WizardSteps;
using Moq;

namespace ClipStudio.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="SourceFoldersStepViewModel"/> covering path validation,
/// repository calls, watcher interactions, and the Browse event.
/// </summary>
public sealed class SourceFoldersStepViewModelTests : IDisposable
{
    private readonly Mock<ISourceFolderRepository> _repoMock = new();
    private readonly Mock<ILibraryWatcherService>  _watcherMock = new();
    private readonly SourceFoldersStepViewModel    _vm;

    // A temporary directory that genuinely exists on disk
    private readonly string _tempDir;

    public SourceFoldersStepViewModelTests()
    {
        _vm      = new SourceFoldersStepViewModel(_repoMock.Object, _watcherMock.Object);
        _tempDir = Path.Combine(Path.GetTempPath(), $"ClipStudioTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ---- Validation: empty path ----

    [Fact]
    public async Task AddFolderCommand_EmptyPath_SetsFolderError()
    {
        _vm.NewFolderPath = string.Empty;

        await _vm.AddFolderCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(_vm.FolderError));
        Assert.Empty(_vm.AddedFolders);
    }

    [Fact]
    public async Task AddFolderCommand_WhitespacePath_SetsFolderError()
    {
        _vm.NewFolderPath = "   ";

        await _vm.AddFolderCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(_vm.FolderError));
        Assert.Empty(_vm.AddedFolders);
    }

    // ---- Validation: non-existent path ----

    [Fact]
    public async Task AddFolderCommand_NonExistentPath_SetsFolderError()
    {
        _vm.NewFolderPath = Path.Combine(Path.GetTempPath(), $"DoesNotExist_{Guid.NewGuid():N}");

        await _vm.AddFolderCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(_vm.FolderError));
        Assert.Empty(_vm.AddedFolders);
    }

    // ---- Happy path: valid folder ----

    [Fact]
    public async Task AddFolderCommand_ValidPath_CallsRepositoryAddAsync()
    {
        _vm.NewFolderPath = _tempDir;

        await _vm.AddFolderCommand.ExecuteAsync(null);

        _repoMock.Verify(r => r.AddAsync(It.Is<SourceFolder>(f => f.Path == _tempDir && f.IsActive), default), Times.Once);
    }

    [Fact]
    public async Task AddFolderCommand_ValidPath_CallsWatcherStartWatching()
    {
        _vm.NewFolderPath = _tempDir;

        await _vm.AddFolderCommand.ExecuteAsync(null);

        _watcherMock.Verify(w => w.StartWatching(_tempDir, It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task AddFolderCommand_ValidPath_AddsToAddedFolders()
    {
        _vm.NewFolderPath = _tempDir;

        await _vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Single(_vm.AddedFolders);
        Assert.Equal(_tempDir, _vm.AddedFolders[0].Path);
    }

    [Fact]
    public async Task AddFolderCommand_ValidPath_ClearsFolderError()
    {
        _vm.NewFolderPath = _tempDir;

        await _vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Null(_vm.FolderError);
    }

    [Fact]
    public async Task AddFolderCommand_ValidPath_ClearsNewFolderPath()
    {
        _vm.NewFolderPath = _tempDir;

        await _vm.AddFolderCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, _vm.NewFolderPath);
    }

    // ---- Validation: duplicate path ----

    [Fact]
    public async Task AddFolderCommand_DuplicatePath_SetsFolderError()
    {
        _vm.NewFolderPath = _tempDir;
        await _vm.AddFolderCommand.ExecuteAsync(null);

        _vm.NewFolderPath = _tempDir;
        await _vm.AddFolderCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(_vm.FolderError));
        Assert.Single(_vm.AddedFolders); // second add was rejected
    }

    [Fact]
    public async Task AddFolderCommand_DuplicatePath_CaseInsensitive_SetsFolderError()
    {
        _vm.NewFolderPath = _tempDir;
        await _vm.AddFolderCommand.ExecuteAsync(null);

        _vm.NewFolderPath = _tempDir.ToUpperInvariant();
        await _vm.AddFolderCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(_vm.FolderError));
        Assert.Single(_vm.AddedFolders);
    }

    // ---- Remove ----

    [Fact]
    public async Task RemoveCommand_RemovesFolderFromCollection()
    {
        _repoMock.Setup(r => r.DeleteAsync(It.IsAny<int>(), default)).Returns(Task.CompletedTask);
        _vm.NewFolderPath = _tempDir;
        await _vm.AddFolderCommand.ExecuteAsync(null);
        var item = _vm.AddedFolders[0];

        await item.RemoveCommand.ExecuteAsync(null);

        Assert.Empty(_vm.AddedFolders);
    }

    [Fact]
    public async Task RemoveCommand_CallsWatcherStopWatching()
    {
        _repoMock.Setup(r => r.DeleteAsync(It.IsAny<int>(), default)).Returns(Task.CompletedTask);
        _vm.NewFolderPath = _tempDir;
        await _vm.AddFolderCommand.ExecuteAsync(null);
        var item = _vm.AddedFolders[0];

        await item.RemoveCommand.ExecuteAsync(null);

        _watcherMock.Verify(w => w.StopWatching(_tempDir), Times.Once);
    }

    // ---- Browse event ----

    [Fact]
    public void BrowseCommand_RaisesBrowseRequestedEvent()
    {
        var raised = false;
        _vm.BrowseRequested += () => raised = true;

        _vm.BrowseCommand.Execute(null);

        Assert.True(raised);
    }
}
