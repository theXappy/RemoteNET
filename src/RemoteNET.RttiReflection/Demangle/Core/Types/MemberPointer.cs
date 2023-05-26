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

namespace Reko.Core.Types
{
	/// <summary>
	/// Represents a member pointer type, as in C++: int foo::*bar makes bar a pointer to a member of foo.
	/// </summary>
    /// <remarks>
    /// x86 "near pointers" are modelled by this data type.
    /// </remarks>
	public class MemberPointer : DataType
	{
		public MemberPointer(DataType basePtr, DataType pointee, int bitSize)
            : base(Domain.Offset)
		{
			this.Pointee = pointee;
			this.BasePointer = basePtr;
			this.BitSize = bitSize;
		}

        public override void Accept(IDataTypeVisitor v)
        {
            v.VisitMemberPointer(this);
        }

        public override T Accept<T>(IDataTypeVisitor<T> v)
        {
            return v.VisitMemberPointer(this);
        }

        public override DataType Clone(IDictionary<DataType, DataType>? clonedTypes)
		{
            return new MemberPointer(BasePointer.Clone(clonedTypes), Pointee.Clone(clonedTypes), BitSize)
            {
                Qualifier = this.Qualifier
            };
		}

        public override int BitSize { get; }
        
        public override bool IsComplex => true;

		/// <summary>
		/// The offset part of a member pointer.
		/// </summary>
		public DataType Pointee { get; set; }

		public override int Size
		{
            get { return (BitSize + (BitsPerByte - 1)) / BitsPerByte; }
            set { ThrowBadSize(); }
		}

		public DataType BasePointer { get; set; }
	}
}
