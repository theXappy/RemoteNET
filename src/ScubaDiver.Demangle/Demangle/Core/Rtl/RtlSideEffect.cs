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

using ScubaDiver.Demangle.Demangle.Core.Expressions;

namespace ScubaDiver.Demangle.Demangle.Core.Rtl
{
    public sealed class RtlSideEffect : RtlInstruction
    {
        public RtlSideEffect(Expression sideEffect, InstrClass iclass)
        {
            this.Expression = sideEffect;
            this.Class = iclass;
        }

        public Expression Expression { get; }

        public override T Accept<T>(RtlInstructionVisitor<T> visitor)
        {
            return visitor.VisitSideEffect(this);
        }

        public override T Accept<T, C>(IRtlInstructionVisitor<T, C> visitor, C context)
        {
            return visitor.VisitSideEffect(this, context);
        }

        protected override void WriteInner(TextWriter writer)
        {
            writer.Write(Expression);
        }
    }
}
