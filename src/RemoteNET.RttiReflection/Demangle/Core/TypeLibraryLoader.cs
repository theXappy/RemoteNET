#region License
/* 
 * Copyright (C) 1999-2023 Pavel Tomin.
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

using Reko.Core.Serialization;
using System;
using System.IO;

namespace Reko.Core
{
    public class TypeLibraryLoader : MetadataLoader
    {
        private Stream stream;

        public TypeLibraryLoader(IServiceProvider services, ImageLocation imageLocation, byte[] bytes) 
            : base(services, imageLocation, bytes)
        {
            this.stream = new MemoryStream(bytes);
        }

        public override TypeLibrary Load(IPlatform platform, TypeLibrary dstLib)
        {
            var ser = SerializedLibrary.CreateSerializer();
            var slib = (SerializedLibrary) ser.Deserialize(stream)!;
            var tldser = new TypeLibraryDeserializer(platform, true, dstLib);
            var tlib = tldser.Load(slib);
            return tlib;
        }
    }
}
