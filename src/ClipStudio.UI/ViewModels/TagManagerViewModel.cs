using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClipStudio.Application.Interfaces;
using ClipStudio.Core.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// View model for the Tag Manager page (General-type tags only).
/// Provides a filterable hierarchical list with inline create / edit / delete operations
/// and a visual color palette for easy color selection.
/// </summary>
public sealed partial class TagManagerViewModel : ViewModelBase
{
    private readonly ITagService _tagService;

    // ---- Preset color palette ----

    private static readonly string[] PaletteColors =
    [
        "#E74C3C", "#E67E22", "#F1C40F", "#2ECC71", "#1ABC9C",
        "#3498DB", "#9B59B6", "#E91E63", "#FF5722", "#8BC34A",
        "#00BCD4", "#2196F3", "#673AB7", "#607D8B", "#795548",
        "#F44336", "#4CAF50", "#FF9800"
    ];

    // ---- Observable state ----

    /// <summary>Gets the full collection of General tag rows currently displayed.</summary>
    public ObservableCollection<TagRowViewModel> Tags { get; } = new();

    /// <summary>Gets the preset color swatches for the edit form palette.</summary>
    public IReadOnlyList<ColorSwatchViewModel> ColorPalette { get; }

    /// <summary>Gets the list of tags available as parent options in the edit form.</summary>
    public ObservableCollection<ParentTagOptionViewModel> ParentTagOptions { get; } = new();

    /// <summary>Gets or sets the filter string applied to the tag list.</summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>Gets or sets a value indicating whether a background load is running.</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Gets or sets a value indicating whether the create / edit form is visible.</summary>
    [ObservableProperty]
    private bool _isEditing;

    /// <summary>Gets or sets the tag identifier being edited, or null for a new tag.</summary>
    [ObservableProperty]
    private int? _editingTagId;

    /// <summary>Gets or sets the tag name in the edit form.</summary>
    [ObservableProperty]
    private string _editName = string.Empty;

    /// <summary>Gets or sets the hex colour in the edit form.</summary>
    [ObservableProperty]
    private string _editColor = "#607D8B";

    /// <summary>Gets or sets the description in the edit form.</summary>
    [ObservableProperty]
    private string _editDescription = string.Empty;

    /// <summary>Gets or sets the selected parent tag option in the edit form.</summary>
    [ObservableProperty]
    private ParentTagOptionViewModel? _selectedParentTagOption;

    /// <summary>Gets or sets the edit form title (either "New Tag" or "Edit Tag").</summary>
    [ObservableProperty]
    private string _editFormTitle = "New Tag";

    /// <summary>Gets or sets a validation / error message from the last save attempt.</summary>
    [ObservableProperty]
    private string? _saveError;

    // ---- Commands ----

    /// <summary>Gets the command that loads all General tags from the database.</summary>
    public IAsyncRelayCommand LoadCommand { get; }

    /// <summary>Gets the command that opens the create-new-tag form.</summary>
    public IRelayCommand BeginCreateCommand { get; }

    /// <summary>Gets the command that cancels the edit form without saving.</summary>
    public IRelayCommand CancelEditCommand { get; }

    /// <summary>Gets the command that saves the current edit form (create or update).</summary>
    public IAsyncRelayCommand SaveCommand { get; }

    /// <summary>
    /// Initialises a new <see cref="TagManagerViewModel"/>.
    /// </summary>
    /// <param name="tagService">The application-layer tag service.</param>
    public TagManagerViewModel(ITagService tagService)
    {
        _tagService = tagService;

        ColorPalette = PaletteColors
            .Select(c => new ColorSwatchViewModel(c, SelectColor))
            .ToList();

        LoadCommand        = new AsyncRelayCommand(LoadAsync);
        BeginCreateCommand = new RelayCommand(BeginCreate);
        CancelEditCommand  = new RelayCommand(CancelEdit);
        SaveCommand        = new AsyncRelayCommand(SaveAsync);
    }

    // ---- Load ----

    /// <summary>
    /// Loads all General tags from the database and refreshes the displayed list,
    /// preserving parent-child hierarchy order with visual indentation.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        SaveError = null;

        try
        {
            var all = await _tagService.GetAllAsync();
            Tags.Clear();

            var filter      = FilterText.Trim();
            var generalTags = string.IsNullOrEmpty(filter)
                ? all.Where(t => t.Type == Core.Enums.TagType.General).ToList()
                : all.Where(t => t.Type == Core.Enums.TagType.General
                              && t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            // Build hierarchical display order using in-memory tree traversal.
            var byId = generalTags.ToDictionary(t => t.Id);

            void AddWithChildren(Tag tag, int depth)
            {
                Tags.Add(new TagRowViewModel(tag, BeginEdit, DeleteTagAsync, depth));
                foreach (var child in generalTags
                             .Where(t => t.ParentTagId == tag.Id)
                             .OrderBy(t => t.Name))
                    AddWithChildren(child, depth + 1);
            }

            foreach (var root in generalTags
                         .Where(t => t.ParentTagId == null || !byId.ContainsKey(t.ParentTagId.Value))
                         .OrderBy(t => t.Name))
                AddWithChildren(root, 0);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Reloads the list when the filter text changes.</summary>
    partial void OnFilterTextChanged(string value) => LoadCommand.Execute(null);

    // ---- Color selection ----

    private void SelectColor(string hex) => EditColor = hex;

    // ---- Create / Edit form ----

    private void BeginCreate()
    {
        EditingTagId            = null;
        EditFormTitle           = "New Tag";
        EditName                = string.Empty;
        EditColor               = "#607D8B";
        EditDescription         = string.Empty;
        SelectedParentTagOption = null;
        SaveError               = null;
        PopulateParentOptions(excludeId: null);
        IsEditing = true;
    }

    private void BeginEdit(TagRowViewModel row)
    {
        EditingTagId    = row.TagId;
        EditFormTitle   = "Edit Tag";
        EditName        = row.Name;
        EditColor       = row.Color;
        EditDescription = row.Description ?? string.Empty;
        SaveError       = null;
        PopulateParentOptions(excludeId: row.TagId);

        // Pre-select the no-parent option by default; parent restoration requires loading the tag.
        SelectedParentTagOption = ParentTagOptions.FirstOrDefault();
        IsEditing = true;
    }

    private void PopulateParentOptions(int? excludeId)
    {
        ParentTagOptions.Clear();
        ParentTagOptions.Add(new ParentTagOptionViewModel(null, "(No parent)"));

        foreach (var tag in Tags)
        {
            if (tag.TagId == excludeId) continue;
            ParentTagOptions.Add(new ParentTagOptionViewModel(tag.TagId, tag.Name));
        }

        SelectedParentTagOption = ParentTagOptions[0];
    }

    private void CancelEdit()
    {
        IsEditing = false;
        SaveError = null;
    }

    // ---- Save ----

    private async Task SaveAsync()
    {
        SaveError = null;

        var name = EditName.Trim();
        if (string.IsNullOrEmpty(name))
        {
            SaveError = "Name is required.";
            return;
        }

        var color    = string.IsNullOrWhiteSpace(EditColor) ? "#607D8B" : EditColor.Trim();
        var parentId = SelectedParentTagOption?.TagId;
        var desc     = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim();

        try
        {
            if (EditingTagId.HasValue)
                await _tagService.UpdateAsync(EditingTagId.Value, name, color, desc, parentId);
            else
                await _tagService.CreateAsync(name, color, desc, parentId);

            IsEditing = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SaveError = ex.Message;
        }
    }

    // ---- Delete ----

    private async Task DeleteTagAsync(TagRowViewModel row)
    {
        try
        {
            await _tagService.DeleteAsync(row.TagId);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            SaveError = ex.Message;
        }
    }
}
