#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Editor;
using Sandbox;

namespace Editor.Analysis.Internal.Navigation;

/// <summary>
/// Robust IDE code navigation resolver for s&box Editor (VSCode, Rider, Visual Studio).
/// Resolves project-relative, library-relative, and absolute source paths.
/// </summary>
public static class CodeNavigator
{
    private static readonly Dictionary<string, string> PathCache = new( StringComparer.OrdinalIgnoreCase );

    /// <summary>
    /// Opens the specified file in the active external code editor at target line and column.
    /// </summary>
    /// <param name="filePath">Absolute or relative source file path.</param>
    /// <param name="line">1-based target line number.</param>
    /// <param name="column">1-based target column number.</param>
    /// <returns>True if the file was resolved and opened successfully; otherwise false.</returns>
    public static bool OpenFile( string? filePath, int line = 1, int column = 0 )
    {
        if ( string.IsNullOrWhiteSpace( filePath ) )
            return false;

        string? resolvedPath = ResolvePath( filePath );
        if ( resolvedPath == null || !File.Exists( resolvedPath ) )
        {
            Log.Warning( $"[CodeNavigator] Could not resolve source file on disk: '{filePath}'" );
            return false;
        }

        int targetLine = Math.Max( 1, line );
        int targetColumn = Math.Max( 0, column );

        CodeEditor.OpenFile( resolvedPath, targetLine, targetColumn );
        return true;
    }

    /// <summary>
    /// Resolves any relative, library-relative, or absolute path to a fully qualified on-disk path.
    /// </summary>
    public static string? ResolvePath( string filePath )
    {
        if ( string.IsNullOrWhiteSpace( filePath ) )
            return null;

        string normalized = filePath.Replace( '\\', '/' ).Trim();

        // 0. Cache hit
        if ( PathCache.TryGetValue( normalized, out string? cached ) && File.Exists( cached ) )
            return cached;

        // 1. Direct absolute path check
        if ( Path.IsPathRooted( normalized ) && File.Exists( normalized ) )
        {
            string full = Path.GetFullPath( normalized );
            PathCache[normalized] = full;
            return full;
        }

        // 2. Resolve against current Project root and Libraries
        if ( Project.Current != null )
        {
            string projectRoot = Project.Current.RootDirectory.FullName;

            // Direct relative
            string candidate = Path.GetFullPath( Path.Combine( projectRoot, normalized ) );
            if ( File.Exists( candidate ) )
            {
                PathCache[normalized] = candidate;
                return candidate;
            }

            // Trim leading separators
            string trimmed = normalized.TrimStart( '/', '.' );
            candidate = Path.GetFullPath( Path.Combine( projectRoot, trimmed ) );
            if ( File.Exists( candidate ) )
            {
                PathCache[normalized] = candidate;
                return candidate;
            }

            // Check inside Libraries/ directory
            string libraryCandidate = Path.GetFullPath( Path.Combine( projectRoot, "Libraries", trimmed ) );
            if ( File.Exists( libraryCandidate ) )
            {
                PathCache[normalized] = libraryCandidate;
                return libraryCandidate;
            }

            // 3. Fallback: Search in Project tree by file name / relative suffix
            string fileName = Path.GetFileName( normalized );
            if ( !string.IsNullOrEmpty( fileName ) && Directory.Exists( projectRoot ) )
            {
                try
                {
                    var matches = Directory.EnumerateFiles( projectRoot, fileName, SearchOption.AllDirectories );
                    foreach ( var match in matches )
                    {
                        string matchNorm = match.Replace( '\\', '/' );
                        if ( matchNorm.EndsWith( normalized, StringComparison.OrdinalIgnoreCase ) || matchNorm.EndsWith( fileName, StringComparison.OrdinalIgnoreCase ) )
                        {
                            string fullMatch = Path.GetFullPath( match );
                            PathCache[normalized] = fullMatch;
                            return fullMatch;
                        }
                    }
                }
                catch
                {
                    // Ignore search permission / io exceptions
                }
            }
        }

        // 4. Fallback check relative to process working directory
        if ( File.Exists( normalized ) )
        {
            string full = Path.GetFullPath( normalized );
            PathCache[normalized] = full;
            return full;
        }

        return null;
    }
}