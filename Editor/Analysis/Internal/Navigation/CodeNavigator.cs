#nullable enable
using System.IO;
using Editor;

namespace Editor.Analysis.Internal.Navigation;

/// <summary>
/// Handles IDE code navigation using s&box Editor CodeEditor API.
/// </summary>
public static class CodeNavigator
{
    /// <summary>
    /// Opens the specified file in user's IDE (VSCode, Rider, Visual Studio) at the target line and column.
    /// </summary>
    /// <param name="filePath">Absolute or project-relative path to the source file.</param>
    /// <param name="line">Target line number (1-based).</param>
    /// <param name="column">Target column number (1-based).</param>
    /// <returns>True if the file was found and opened successfully; otherwise false.</returns>
    public static bool OpenFile( string? filePath, int line = 0, int column = 0 )
    {
        if ( string.IsNullOrWhiteSpace( filePath ) )
            return false;

        // Ensure path exists before requesting CodeEditor to open it
        if ( !File.Exists( filePath ) )
            return false;

        CodeEditor.OpenFile( filePath, line, column );
        return true;
    }
}