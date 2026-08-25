#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Sandbox;

namespace Editor.Analysis.Internal.Extractors;

/// <summary>
/// Scans and enumerates all active user project source code files.
/// </summary>
public static class ProjectSourceScanner
{
    private static readonly HashSet<string> ValidExtensions = new( StringComparer.OrdinalIgnoreCase )
    {
        ".cs",
        ".razor"
    };

    /// <summary>
    /// Enumerates all user source files in the project's code directory.
    /// </summary>
    public static IEnumerable<string> EnumerateSourceFiles()
    {
        var project = Project.Current;
        if ( project?.RootDirectory == null || !project.RootDirectory.Exists )
            yield break;

        string codePath = project.GetCodePath();
        if ( string.IsNullOrEmpty( codePath ) || !Directory.Exists( codePath ) )
        {
            codePath = project.RootDirectory.FullName;
        }

        foreach ( var filePath in Directory.EnumerateFiles( codePath, "*.*", SearchOption.AllDirectories ) )
        {
            string ext = Path.GetExtension( filePath );
            if ( !ValidExtensions.Contains( ext ) )
                continue;

            string normalized = filePath.Replace( '\\', '/' );

            // Ignore compilation artifacts, git folders, and internal caches
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