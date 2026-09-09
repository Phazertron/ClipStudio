namespace ClipStudio.Application.Models;

/// <summary>
/// How an incoming file was recognised as something the library may already hold.
/// </summary>
/// <remarks>
/// The two are not equivalent and the user is told which applies. A name match is a warning: two
/// unrelated recordings can share a name. A content match is a fact: the bytes are the same.
/// </remarks>
public enum DuplicateMatchKind
{
    /// <summary>Another clip has the same file name, in a different folder. Contents may differ.</summary>
    FileName,

    /// <summary>Another clip has identical contents, confirmed by a full hash.</summary>
    Content,
}
