/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Jan Nieuwenhuizen <janneke@gnu.org>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeBrix.LilyPort.Flower; //was previously: flower/file-name.cc, flower/include/file-name.hh, flower/file-path.cc, flower/include/file-path.hh;
// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port:
//   - translated from C++17 to C# targeting net10.0
//   - the Windows drive-letter handling that upstream guards with #ifdef __MINGW32__
//     is compiled unconditionally here, since one managed binary serves every
//     platform. It is inert on paths that carry no drive letter.
//   - System.IO.Path is deliberately NOT used for the parsing. LilyPond's rules are
//     its own -- notably that "." and ".." are treated as DIRECTORY components with
//     no basename, and that the extension excludes its dot -- and .ly files depend
//     on them through \include.

/// <summary>
/// A parsed file name, split into root (a Windows drive), directory, basename and
/// extension. Reassembling the parts reproduces the original.
/// </summary>
public sealed class FileName
{
    private const char DirectorySeparator = '/';
    private const char RootSeparator = ':';

    /// <summary>Initializes an empty file name.</summary>
    public FileName()
        : this(string.Empty)
    {
    }

    /// <summary>Parses a file name into its parts.</summary>
    /// <param name="fileName">The name to parse.</param>
    public FileName(string fileName)
    {
        Root = string.Empty;
        Directory = string.Empty;
        Base = string.Empty;
        Extension = string.Empty;

        string working = Slashify(fileName ?? string.Empty);

        // A drive letter, on the platforms that have them.
        int rootIndex = working.IndexOf(RootSeparator);
        if (rootIndex >= 0)
        {
            Root = working.Substring(0, rootIndex);
            working = working.Substring(rootIndex + 1);
        }

        IsAbsolute = working.IndexOf(DirectorySeparator) == 0;

        int lastSeparator = working.LastIndexOf(DirectorySeparator);
        if (lastSeparator >= 0)
        {
            Directory = working.Substring(0, lastSeparator);
            working = working.Substring(lastSeparator + 1);
        }

        // "." and ".." name a directory, not a base name with an extension --
        // splitting them on the dot would be wrong.
        if (working == "." || working == "..")
        {
            if (Directory.Length != 0)
            {
                Directory += DirectorySeparator;
            }

            Directory += working;
            return;
        }

        int dot = working.LastIndexOf('.');
        if (dot >= 0)
        {
            Base = working.Substring(0, dot);
            Extension = working.Substring(dot + 1);
        }
        else
        {
            Base = working;
        }
    }

    /// <summary>Gets or sets the root, which is a drive letter where the platform has one.</summary>
    public string Root { get; set; }

    /// <summary>Gets or sets the directory part, without a trailing separator.</summary>
    public string Directory { get; set; }

    /// <summary>Gets or sets the base name, without directory or extension.</summary>
    public string Base { get; set; }

    /// <summary>Gets or sets the extension, WITHOUT its leading dot.</summary>
    public string Extension { get; set; }

    /// <summary>Gets a value indicating whether the name is absolute.</summary>
    public bool IsAbsolute { get; private set; }

    /// <summary>Normalizes backslashes to forward slashes.</summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The path using forward slashes only.</returns>
    public static string Slashify(string path) => (path ?? string.Empty).Replace('\\', '/');

    /// <summary>Gets the directory portion, including the root when present.</summary>
    /// <returns>The directory part.</returns>
    public string DirectoryPart()
    {
        StringBuilder builder = new StringBuilder();
        if (Root.Length != 0)
        {
            builder.Append(Root).Append(RootSeparator);
        }

        builder.Append(Directory);
        return builder.ToString();
    }

    /// <summary>Gets the file portion: base name plus extension.</summary>
    /// <returns>The file part.</returns>
    public string FilePart()
    {
        StringBuilder builder = new StringBuilder(Base);
        if (Extension.Length != 0)
        {
            builder.Append('.').Append(Extension);
        }

        return builder.ToString();
    }

    /// <summary>Reassembles the whole name.</summary>
    /// <returns>The full path.</returns>
    public override string ToString()
    {
        string directory = DirectoryPart();
        string file = FilePart();
        if (file.Length != 0 && Directory.Length != 0)
        {
            directory += DirectorySeparator;
        }

        return directory + file;
    }

    /// <summary>Returns this name made absolute against a working directory.</summary>
    /// <param name="currentWorkingDirectory">The directory to resolve against.</param>
    /// <returns>An absolute file name.</returns>
    public FileName Absolute(string currentWorkingDirectory)
    {
        if (IsAbsolute)
        {
            return this;
        }

        FileName result = new FileName(ToString())
        {
            Root = Root,
        };

        string prefix = currentWorkingDirectory ?? string.Empty;
        if (Directory.Length != 0)
        {
            result.Directory = prefix.Length != 0
                ? prefix + DirectorySeparator + Directory
                : Directory;
        }
        else
        {
            result.Directory = prefix;
        }

        result.IsAbsolute = true;
        return result;
    }

    /// <summary>
    /// Collapses <c>//</c>, removes interior <c>.</c> components, and resolves
    /// <c>..</c> against the preceding component.
    /// </summary>
    /// <returns>The canonicalized name.</returns>
    public FileName Canonicalized()
    {
        FileName result = new FileName(ToString())
        {
            Root = Root,
            Base = Base,
            Extension = Extension,
            IsAbsolute = IsAbsolute,
        };

        string directory = Directory;
        while (directory.Contains("//", StringComparison.Ordinal))
        {
            directory = directory.Replace("//", "/", StringComparison.Ordinal);
        }

        string[] components = directory.Split(DirectorySeparator);
        List<string> kept = new List<string>();
        for (int i = 0; i < components.Length; i++)
        {
            if (i != 0 && components[i] == ".")
            {
                continue;
            }

            if (kept.Count != 0 && components[i] == "..")
            {
                string removed = kept[kept.Count - 1];
                kept.RemoveAt(kept.Count - 1);

                // Upstream keeps the path anchored when the stack empties: popping
                // "." leaves "..", popping anything else leaves ".".
                if (kept.Count == 0)
                {
                    kept.Add(removed == "." ? ".." : ".");
                }

                continue;
            }

            kept.Add(components[i]);
        }

        result.Directory = string.Join(DirectorySeparator.ToString(), kept);
        return result;
    }

    /// <summary>Returns the directory portion of a path string.</summary>
    /// <param name="fileName">The path to split.</param>
    /// <returns>The directory, or an empty string when there is none.</returns>
    public static string DirName(string fileName) => new FileName(fileName).DirectoryPart();

    /// <summary>Returns the process's current working directory.</summary>
    /// <returns>The working directory.</returns>
    public static string GetWorkingDirectory() => System.IO.Directory.GetCurrentDirectory();
}

/// <summary>
/// An ordered list of directories to search, analogous to <c>$PATH</c>. LilyPond uses
/// it to resolve <c>\include</c> and to find fonts and Scheme files.
/// </summary>
public sealed class FilePath
{
    private readonly List<string> _directories = new List<string>();

    /// <summary>Gets the directories, in search order.</summary>
    public IReadOnlyList<string> Directories => _directories;

    /// <summary>Appends a directory to the end of the search order.</summary>
    /// <param name="directory">The directory to add.</param>
    public void Append(string directory) => _directories.Add(directory);

    /// <summary>Inserts a directory at the front of the search order.</summary>
    /// <param name="directory">The directory to add.</param>
    public void Prepend(string directory) => _directories.Insert(0, directory);

    /// <summary>Appends a directory only when it exists.</summary>
    /// <param name="directory">The directory to try.</param>
    /// <returns><see langword="true"/> when the directory existed and was added.</returns>
    public bool TryAppend(string directory)
    {
        if (IsDirectory(directory))
        {
            Append(directory);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses a separator-delimited path list. Both the platform separator and the
    /// colon are accepted, matching upstream's behaviour on POSIX.
    /// </summary>
    /// <param name="path">The path list to parse.</param>
    public void ParsePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        foreach (string part in path.Split(new[] { Path.PathSeparator, ':' }, StringSplitOptions.None))
        {
            Append(part);
        }
    }

    /// <summary>Searches for a file by name.</summary>
    /// <param name="name">The file name to find.</param>
    /// <returns>The full path, or an empty string when not found.</returns>
    public string Find(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        // An absolute name is returned as-is when it exists, without searching.
        if (new FileName(name).IsAbsolute)
        {
            return IsFile(name) ? name : string.Empty;
        }

        foreach (string directory in _directories)
        {
            string candidate = directory.Length != 0 ? directory + "/" + name : name;
            if (IsFile(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    /// <summary>Searches for a file, trying each extension in turn.</summary>
    /// <param name="name">The base file name.</param>
    /// <param name="extensions">Extensions to try, without leading dots. An empty entry means "as-is".</param>
    /// <returns>The full path, or an empty string when not found.</returns>
    public string Find(string name, IEnumerable<string> extensions)
    {
        if (extensions == null)
        {
            return Find(name);
        }

        FileName parsed = new FileName(name);
        foreach (string extension in extensions)
        {
            parsed.Extension = extension ?? string.Empty;
            string found = Find(parsed.ToString());
            if (found.Length != 0)
            {
                return found;
            }
        }

        return string.Empty;
    }

    /// <summary>Determines whether a path names an existing file.</summary>
    /// <param name="fileName">The path to test.</param>
    /// <returns><see langword="true"/> when the file exists.</returns>
    public static bool IsFile(string fileName)
        => !string.IsNullOrEmpty(fileName) && File.Exists(fileName);

    /// <summary>Determines whether a path names an existing directory.</summary>
    /// <param name="directoryName">The path to test.</param>
    /// <returns><see langword="true"/> when the directory exists.</returns>
    public static bool IsDirectory(string directoryName)
        => !string.IsNullOrEmpty(directoryName) && System.IO.Directory.Exists(directoryName);

    /// <summary>Returns the search path as a separator-delimited string.</summary>
    /// <returns>The joined path list.</returns>
    public override string ToString() => string.Join(Path.PathSeparator.ToString(), _directories);
}
