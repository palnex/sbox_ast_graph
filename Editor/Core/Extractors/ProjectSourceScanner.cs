using System;
using System.Collections.Generic;
using System.IO;
using Sandbox;

namespace Editor.Core.Extractors;

/// <summary>
/// Resolves project paths and enumerates user source code files (.cs, .razor).
/// </summary>
public static class ProjectSourceScanner
{
    private static readonly HashSet<string> ValidExtensions = new( StringComparer.OrdinalIgnoreCase )
    {
        ".cs",
        ".razor"
    };

    /// <summary>
    /// Finds all source files in the active s&box project, excluding build artifacts and internal caches.
    /// </summary>
    public static IEnumerable<string> EnumerateProjectFiles()
    {
        var project = Project.Current;
        if ( project?.RootDirectory == null || !project.RootDirectory.Exists )
            yield break;

        string rootPath = project.RootDirectory.FullName;

        foreach ( var filePath in Directory.EnumerateFiles( rootPath, "*.*", SearchOption.AllDirectories ) )
        {
            string ext = Path.GetExtension( filePath );
            if ( !ValidExtensions.Contains( ext ) )
                continue;

            string normalized = filePath.Replace( '\\', '/' );

            // Skip compiler artifacts, git folders, and internal caches
            if ( normalized.Contains( "/.sbx/" ) ||
                normalized.Contains( "/bin/" ) ||
                normalized.Contains( "/obj/" ) ||
                normalized.Contains( "/.git/" ) )
            {
                continue;
            }

            yield return filePath;
        }
    }
}