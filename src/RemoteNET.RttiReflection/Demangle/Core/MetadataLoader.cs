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
using System.ComponentModel.Design;

namespace Reko.Core
{
    /// <summary>
    /// Base class for files that know how to load metadata from some file format.
    /// </summary>
    public abstract class MetadataLoader
    {
        public MetadataLoader(IServiceProvider services, ImageLocation imagelocation, byte[] bytes)
        {
            this.Services = services;
            this.Location = imagelocation;
        }

        public IServiceProvider Services { get; }

        public ImageLocation Location { get; }

        /// <summary>
        /// Loads metadata from the file specified in the constructor.
        /// </summary>
        /// <param name="platform"></param>
        /// <param name="dstLib"></param>
        /// <returns></returns>
        public abstract TypeLibrary Load(IPlatform platform, TypeLibrary dstLib);

        public virtual TypeLibrary Load(IPlatform platform, string? moduleName, TypeLibrary dstLib)
        {
            return Load(platform, dstLib);
        }
    }

    public class NullMetadataLoader : MetadataLoader
    {
        public NullMetadataLoader()
            : base(new ServiceContainer(), ImageLocation.FromUri(""), Array.Empty<byte>())
        {
        }

        public override TypeLibrary Load(IPlatform platform, TypeLibrary dstLib)
        {
            return dstLib;
        }
    }
}
