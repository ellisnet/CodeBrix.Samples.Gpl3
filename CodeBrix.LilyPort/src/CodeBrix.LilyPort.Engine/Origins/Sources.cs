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
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Origins; //was previously: lily/sources.cc, lily/include/sources.hh;

// Modified by Jeremy Ellis on 2026-08-04 as part of the CodeBrix port.

/// <summary>
/// The set of source files opened so far, and the include path they are looked up on.
/// <para>
/// Every file the run has touched stays here, including ones already finished with,
/// because an <see cref="Input"/> made while a file was open must still be able to quote
/// it when the error surfaces much later.
/// </para>
/// </summary>
public sealed class Sources
{
    private readonly List<SourceFile> _sourceFiles = new List<SourceFile>();

    /// <summary>Initializes a source set searching an empty path.</summary>
    public Sources()
        : this(new FilePath())
    {
    }

    /// <summary>Initializes a source set searching a given path.</summary>
    /// <param name="path">The include path to search.</param>
    public Sources(FilePath path)
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>Gets the include path files are looked up on.</summary>
    public FilePath Path { get; }

    /// <summary>Gets every source file opened so far, in the order they were opened.</summary>
    public IReadOnlyList<SourceFile> SourceFiles => _sourceFiles;

    /// <summary>
    /// Opens a file, looking first beside the file currently being parsed and then along
    /// the include path.
    /// </summary>
    /// <param name="fileName">The file to open.</param>
    /// <param name="currentDirectory">
    /// The directory of the file being parsed, searched first; may be
    /// <see langword="null"/> or empty.
    /// </param>
    /// <returns>The opened file, or <see langword="null"/> when it could not be found.</returns>
    public SourceFile GetFile(string fileName, string currentDirectory)
    {
        if (fileName == null)
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        string resolved = fileName;

        // "-" is stdin, and is deliberately NOT put through path resolution.
        if (!string.Equals(fileName, "-", StringComparison.Ordinal))
        {
            resolved = FindFullPath(fileName, currentDirectory);
        }

        if (string.IsNullOrEmpty(resolved))
        {
            return null;
        }

        SourceFile file = SourceFile.Open(resolved);
        Add(file);
        return file;
    }

    /// <summary>Resolves a file name against the current directory and then the path.</summary>
    /// <param name="fileName">The file to find.</param>
    /// <param name="currentDirectory">The directory to try first; may be empty.</param>
    /// <returns>The resolved path, or an empty string when it was not found.</returns>
    public string FindFullPath(string fileName, string currentDirectory)
    {
        if (fileName == null)
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        // First, beside the file currently being parsed -- an \include next to its
        // includer wins over one further along the path.
        if (!string.IsNullOrEmpty(currentDirectory)
            && fileName.Length > 0
            && !new FileName(fileName).IsAbsolute)
        {
            string beside = currentDirectory + "/" + fileName;
            if (FilePath.IsFile(beside))
            {
                return beside;
            }
        }

        return Path.Find(fileName);
    }

    /// <summary>Gets the include path as a printable string.</summary>
    /// <returns>The path.</returns>
    public string SearchPath() => Path.ToString();

    /// <summary>Adds an already-open file to the set.</summary>
    /// <param name="sourceFile">The file to add.</param>
    public void Add(SourceFile sourceFile)
    {
        if (sourceFile == null)
        {
            throw new ArgumentNullException(nameof(sourceFile));
        }

        _sourceFiles.Add(sourceFile);
    }

    /// <summary>Appends a directory to the include path.</summary>
    /// <param name="directory">The directory to append.</param>
    public void AppendToPath(string directory) => Path.Append(directory);
}
