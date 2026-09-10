using ClipStudio.Core.Enums;

namespace ClipStudio.UI.ViewModels;

/// <summary>
/// One link type as the picker offers it.
/// </summary>
/// <param name="Type">The relationship this option creates.</param>
/// <param name="Label">The name shown in the picker.</param>
/// <param name="Hint">A short explanation, since the names alone are ambiguous.</param>
/// <remarks>
/// The label here is how the relationship reads when created, which is the direction the user is
/// working in. The clip on the far end may show the opposite wording - that is resolved by
/// <c>ClipLinkService</c>, not here.
/// </remarks>
public sealed record ClipLinkTypeOption(ClipLinkType Type, string Label, string Hint);
