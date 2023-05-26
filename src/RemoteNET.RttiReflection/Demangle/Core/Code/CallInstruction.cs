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
using System.Collections.Generic;
using System.Text;

namespace Reko.Core.Code
{
    /// <summary>
    /// Models a low-level call instruction.
    /// </summary>
    /// <remarks>CallInstructions only exist right after scanning. Subsequent
    /// decompiler phases will replace them with <see cref="Application" /> instances.
    /// </remarks>
    public class CallInstruction : Instruction
    {
        public CallInstruction(Expression callee, CallSite site)
        {
            this.Callee = callee ?? throw new ArgumentNullException(nameof(callee));
            this.CallSite = site;
            this.Definitions = new HashSet<CallBinding>();
            this.Uses = new HashSet<CallBinding>();
        }

        public Expression Callee { get; set; }
        public CallSite CallSite { get; }

        /// <summary>
        /// Set of expressions that reach the call site. These need to be 
        /// reconciled  with the storages actually used by the callee, if these are 
        /// known.
        /// </summary>
        public HashSet<CallBinding> Uses { get; }

        /// <summary>
        /// Set of expressions that the called function defines.
        /// </summary> 
        public HashSet<CallBinding> Definitions { get; }

        public override bool IsControlFlow => false;

        public override Instruction Accept(InstructionTransformer xform)
        {
            return xform.TransformCallInstruction(this);
        }

        public override T Accept<T>(InstructionVisitor<T> visitor)
        {
            return visitor.VisitCallInstruction(this);
        }

        public override T Accept<T, C>(InstructionVisitor<T, C> visitor, C ctx)
        {
            return visitor.VisitCallInstruction(this, ctx);
        }

        public override void Accept(InstructionVisitor v)
        {
            v.VisitCallInstruction(this);
        }
    }

    public class CallBinding
    {
        /// <summary>
        /// The <see cref="Storage"/> used for an argument to a call. The storage
        /// is relative to the caller's frame.
        /// </summary>
        public Storage Storage;

        /// <summary>
        /// The argument expression bound to a parameter of a call.
        /// </summary>
        public Expression Expression;

        /// <summary>
        /// Argument bits used by the callee.
        /// </summary>
        public BitRange BitRange;

        public CallBinding(Storage stg, Expression exp)
        {
            this.Storage = stg;
            this.Expression = exp;
            this.BitRange = new BitRange(0, exp.DataType.BitSize);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("{0}:{1}", Storage, Expression);
            if (BitRange.Lsb != 0 || (uint)BitRange.Msb != Storage.BitSize)
            {
                sb.Append(BitRange.ToString());
            }
            return sb.ToString();
        }
    }
}
