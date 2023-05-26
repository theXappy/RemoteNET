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

using Reko.Core.Expressions;
using Reko.Core.Types;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Reko.Core
{
    public interface IRewriterHost
    {
        /// <summary>
        /// If the binary has a notion of a global register, this value is non-null.
        /// </summary>
        Constant? GlobalRegisterValue { get; }

        /// <summary>
        /// Obtain a reference to the processor architecture with the name <paramref name="archMoniker"/>.
        /// </summary>
        /// <param name="archMoniker">The name of the desired architecture.</param>
        /// <returns></returns>
        IProcessorArchitecture GetArchitecture(string archMoniker);

        /// <summary>
        /// Given an address <paramref name="addrThunk"/>, returns the possible imported thing (procedure or 
        /// global variable) pointed to by Thunk.
        /// </summary>
        /// <param name="addrThunk"></param>
        /// <param name="addrInstr"></param>
        /// <returns></returns>
        Expression? GetImport(Address addrThunk, Address addrInstr);
        ExternalProcedure? GetImportedProcedure(IProcessorArchitecture arch, Address addrThunk, Address addrInstr);
        ExternalProcedure? GetInterceptedCall(IProcessorArchitecture arch, Address addrImportThunk);

        /// <summary>
        /// Read a value of size <paramref name="dt"/> from address <paramref name="addr"/>, 
        /// using the endianness of the <paramref name="arch"/> processor architecture.
        /// </summary>
        public bool TryRead(IProcessorArchitecture arch, Address addr, PrimitiveType dt, [MaybeNullWhen(false)] out Constant value);

        void Error(Address address, string format, params object[] args);
        void Warn(Address address, string format, params object[] args);
    }

    public class NullRewriterHost : IRewriterHost
    {
        public IntrinsicProcedure EnsureIntrinsic(string name, bool hasSideEffect, DataType returnType, int arity)
        {
            throw new NotSupportedException();
        }

        public Constant? GlobalRegisterValue => null;

        public void Error(Address address, string format, params object[] args)
        {
        }

        public IProcessorArchitecture GetArchitecture(string archMoniker)
        {
            throw new NotSupportedException();
        }

        Expression? IRewriterHost.GetImport(Address addrThunk, Address addrInstr)
        {
            return null;
        }

        public ExternalProcedure? GetImportedProcedure(IProcessorArchitecture arch, Address addrThunk, Address addrInstr)
        {
            return null;
        }

        public ExternalProcedure? GetInterceptedCall(IProcessorArchitecture arch, Address addrImportThunk)
        {
            return null;
        }

        public bool TryRead(IProcessorArchitecture arch, Address addr, PrimitiveType dt, [MaybeNullWhen(false)] out Constant value)
        {
            value = null;
            return false;
        }

        public void Warn(Address address, string format, params object[] args)
        {
        }
    }
}

