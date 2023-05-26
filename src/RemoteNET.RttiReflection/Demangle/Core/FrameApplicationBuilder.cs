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

using Reko.Core.Code;
using Reko.Core.Expressions;
using System;
using System.Linq;

namespace Reko.Core
{
    public class FrameApplicationBuilder : ApplicationBuilder, StorageVisitor<Expression>
    {
        protected readonly IProcessorArchitecture arch;
        protected readonly IStorageBinder binder;

        /// <summary>
        /// Creates an application builder that creates references
        /// to the identifiers in the frame.
        /// </summary>
        /// <param name="arch">The processor architecture to use.</param>
        /// <param name="binder">The <see cref="IStorageBinder"/> of the calling procedure.</param>
        /// <param name="site">The call site of the calling instruction.</param>
        public FrameApplicationBuilder(
            IProcessorArchitecture arch,
            IStorageBinder binder,
            CallSite site) : base(site)
        {
            this.arch = arch;
            this.binder = binder;
        }

        public override Expression BindInArg(Storage stg)
        {
            return stg.Accept(this);
        }

        public override Expression? BindReturnValue(Storage? stg)
        {
            if (stg is null)
                return null;
            return stg.Accept(this);
        }

        public override OutArgument BindOutArg(Storage stg)
        {
            var actualArg = stg.Accept(this);
            return new OutArgument(arch.FramePointerType, actualArg);
        }

        #region StorageVisitor<Expression> Members

        public Expression VisitFlagGroupStorage(FlagGroupStorage grf)
        {
            return binder.EnsureFlagGroup(grf.FlagRegister, grf.FlagGroupBits, grf.Name, grf.DataType);
        }

        public virtual Expression VisitFpuStackStorage(FpuStackStorage fpu)
        {
            return binder.EnsureFpuStackVariable(fpu.FpuStackOffset, fpu.DataType);
        }

        public Expression VisitMemoryStorage(MemoryStorage global)
        {
            throw new NotSupportedException(string.Format("A {0} can't be used as a formal parameter.", global.GetType().FullName));
        }

        public Expression VisitOutArgumentStorage(OutArgumentStorage arg)
        {
            return arg.OriginalIdentifier.Storage.Accept(this);
        }

        public Expression VisitRegisterStorage(RegisterStorage reg)
        {
            return binder.EnsureRegister(reg);
        }

        public Expression VisitSequenceStorage(SequenceStorage seq)
        {
            var exps = seq.Elements.Select(e => e.Accept(this) as Identifier).ToArray();
            if (exps.All(e => e != null))
                return binder.EnsureSequence(seq.DataType, exps.Select(i => i!.Storage).ToArray());
            throw new NotImplementedException("Handle case when stack parameter is passed.");
        }

        public Expression VisitStackStorage(StackStorage stack)
        {
            return BindInStackArg(stack, Site.SizeOfReturnAddressOnStack);
        }

        public override Expression BindInStackArg(StackStorage stack, int returnAdjustment)
        {
            if (!arch.IsStackArgumentOffset(stack.StackOffset))
                throw new InvalidOperationException("A local stack variable can't be used as a parameter.");
            var netOffset = stack.StackOffset - returnAdjustment;
            return arch.CreateStackAccess(binder, netOffset, stack.DataType);
        }

        public Expression VisitTemporaryStorage(TemporaryStorage temp)
        {
            throw new NotSupportedException(string.Format("A {0} can't be used as a formal parameter.", temp.GetType().FullName));
        }

        #endregion

    }
}
