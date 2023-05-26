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
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Reko.Core
{
    /// <summary>
    /// A Trampoline is a small stub of instructions that target another 
    /// procedure via an indirect CTI. It is typically used to implement
    /// dynamically linked procedures.
    /// </summary>
    /// <remarks>
    /// Typically, it will be a short linear of sequence ending in an indirect
    /// goto:
    /// <code>
    /// mov r1,g0(got_plt_offset)
    /// jmp r1
    /// </code>
    /// </remarks>
    public class Trampoline
    {
        public Trampoline(Address addrStub, ProcedureBase procedure)
        {
            this.StubAddress = addrStub;
            this.Procedure = procedure;
        }

        /// <summary>
        /// The address of the beginning of the trampoline.
        /// </summary>
        public Address StubAddress { get; }

        /// <summary>
        /// The procedure reached by calling the stub.
        /// </summary>
        public ProcedureBase Procedure { get; }

        public override string ToString()
        {
            return $"Trampoline@{StubAddress} to {Procedure.Name}";
        }
    }
}
