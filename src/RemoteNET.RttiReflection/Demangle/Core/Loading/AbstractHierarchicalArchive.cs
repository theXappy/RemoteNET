#region License
/* 
 * Copyright (C) 1999-2023 John Källén.
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2, or (at your option)
 * any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; see the file COPYING.  If not, write to
 * the Free Software Foundation, 675 Mass Ave, Cambridge, MA 02139, USA.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Reko.Core.Loading
{
    /// <summary>
    /// Handy implementation class for archives that support hierarchical structure.
    /// </summary>
    public abstract class AbstractHierarchicalArchive : IArchive
    {
        private readonly char pathSeparator;
        private readonly IComparer<string> filenameComparer;
        private readonly Dictionary<string, ArchiveDirectoryEntry> root;

        public AbstractHierarchicalArchive(ImageLocation location, char pathSeparator, IComparer<string> comparer)
        {
            this.Location = location;
            this.pathSeparator = pathSeparator;
            this.filenameComparer = comparer;
            this.root = new Dictionary<string, ArchiveDirectoryEntry>();
        }

        public ArchiveDirectoryEntry? this[string path] => GetEntry(path);

        public ImageLocation Location { get; }

        public List<ArchiveDirectoryEntry> RootEntries
            => root.Values
                .OrderBy(e => e.Name, this.filenameComparer)
                .ToList();

        public T Accept<T, C>(ILoadedImageVisitor<T, C> visitor, C context)
            => visitor.VisitArchive(this, context);

        private ArchiveDirectoryEntry? GetEntry(string path)
        {
            var components = path.Split(pathSeparator);
            if (components.Length == 0)
                return null;
            var curDir = this.root;
            ArchiveDirectoryEntry? entry = null;
            foreach (var component in components)
            {
                if (curDir is null || !curDir.TryGetValue(component, out entry))
                    return null;
                curDir = (entry as ArchiveDictionary)?.entries;
            }
            return entry;
        }

        public virtual ArchivedFile AddFile(
            string path, 
            Func<IArchive, ArchiveDirectoryEntry?, string, ArchivedFile> fileCreator)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException(nameof(path));
            var pathSegments = path.Split(pathSeparator);
            var curDir = this.root;
            ArchivedFolder? parentDir = null;
            for (int i = 0; i < pathSegments.Length - 1; ++i)
            {
                string pathSegment = pathSegments[i];
                if (!curDir.TryGetValue(pathSegment, out var entry))
                {
                    entry = parentDir = new ArchiveDictionary(this, pathSegment, parentDir);;
                    curDir.Add(pathSegment, entry);
                }
                if (entry is not ArchiveDictionary nextDir)
                {
                    var badPath = string.Join(pathSeparator, pathSegments.Take(i));
                    throw new InvalidOperationException($"The path {badPath} is not a directory.");
                }
                curDir = nextDir.entries;
            }
            var filename = pathSegments[^1];
            var file = fileCreator(this, parentDir, filename);
            if (!curDir.TryAdd(filename, file))
                throw new InvalidOperationException($"The path {path} already exists.");
            return file;
        }

        public virtual string GetRootPath(ArchiveDirectoryEntry? entry)
        {
            if (entry is null)
                return "";
            var components = new List<string>();
            while (entry is not null)
            {
                components.Add(entry.Name);
                entry = entry.Parent;
            }
            components.Reverse();
            return string.Join(pathSeparator, components);
        }

        public class ArchiveDictionary : ArchivedFolder
        {
            private readonly AbstractHierarchicalArchive archive;
            internal readonly Dictionary<string, ArchiveDirectoryEntry> entries;

            public ArchiveDictionary(AbstractHierarchicalArchive archive, string name, ArchivedFolder? parent)
            {
                this.archive = archive;
                this.Name = name;
                this.Parent = parent;
                this.entries = new Dictionary<string, ArchiveDirectoryEntry>();
            }

            public ICollection<ArchiveDirectoryEntry> Entries
                => entries.Values
                    .OrderBy(e => e.Name, archive.filenameComparer)
                    .ToList();

            public string Name { get; }

            public ArchiveDirectoryEntry? Parent { get; }

            public void AddEntry(string name, ArchiveDirectoryEntry entry)
            {
                entries[name] = entry;
            }
        }

        public List<ArchiveDirectoryEntry> Load(Stream stm)
        {
            throw new NotImplementedException();
        }
    }
}
