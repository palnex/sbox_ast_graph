#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using Sandbox;

namespace Editor.Analysis.Internal.Extractors;

/// <summary>
/// Source file discovery item with dynamic package identification.
/// </summary>
public record SourceFileItem( string FilePath, string PackageName, bool IsLibrary );

/// <summary>
/// Scans the entire game project directory, including all attached libraries, resolving package roots dynamically.
/// </summary>
public static class ProjectSourceScanner
{
    private static readonly HashSet<string> ValidExtensions = new( StringComparer.OrdinalIgnoreCase )
    {
        ".cs",
        ".razor"
    };

    /// <summary>
    /// Enumerates all user code and library source files dynamically with package identity.
    /// </summary>
    public static IEnumerable<SourceFileItem> EnumerateAllProjectFiles()
    {
        var rootDir = Project.Current?.RootDirectory;
        if ( rootDir == null || !rootDir.Exists )
            yield break;

        string rootPath = rootDir.FullName;
        string rootProjectName = Project.Current?.Config?.Ident ?? Path.GetFileName( rootPath );

        foreach ( var filePath in Directory.EnumerateFiles( rootPath, "*.*", SearchOption.AllDirectories ) )
        {
            string ext = Path.GetExtension( filePath );
            if ( !ValidExtensions.Contains( ext ) )
                continue;

            string normalized = filePath.Replace( '\\', '/' );

            // Ignore compilation caches, build artifacts, and git repositories
            if ( normalized.Contains( "/.sbox/" ) ||
                 normalized.Contains( "/.sbx/" ) ||
                 normalized.Contains( "/bin/" ) ||
                 normalized.Contains( "/obj/" ) ||
                 normalized.Contains( "/.git/" ) )
            {
                continue;
            }

            // Dynamically resolve package identity from the nearest .sbproj or folder boundary
            string packageName = ResolvePackageName( filePath, rootPath, rootProjectName );
            bool isLibrary = !string.Equals( packageName, rootProjectName, StringComparison.OrdinalIgnoreCase );

            yield return new SourceFileItem( filePath, packageName, isLibrary );
        }
    }

    /// <summary>
    /// Traverses up directory tree from source file to find the nearest .sbproj package name without hardcoding.
    /// </summary>
    public static string ResolvePackageName( string filePath, string rootPath, string fallbackName )
    {
        try
        {
            var dir = Directory.GetParent( filePath );

            while ( dir != null && dir.FullName.Length >= rootPath.Length )
            {
                // Look for any .sbproj in this folder
                var projFiles = dir.GetFiles( "*.sbproj" );
                if ( projFiles.Length > 0 )
                {
                    return Path.GetFileNameWithoutExtension( projFiles[0].Name );
                }

                // If folder is direct child of a "Libraries" folder, use directory name
                if ( dir.Parent != null && dir.Parent.Name.Equals( "Libraries", StringComparison.OrdinalIgnoreCase ) )
                {
                    return dir.Name;
                }

                dir = dir.Parent;
            }
        }
        catch
        {
            // Fallback
        }

        return fallbackName;
    }
}