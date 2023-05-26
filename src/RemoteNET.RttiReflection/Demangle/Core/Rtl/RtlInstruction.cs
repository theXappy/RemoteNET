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

using System.IO;
using System.Text;

namespace Reko.Core.Rtl
{
    /// <summary>
    /// RtlInstructions are the low-level register-transfer instructions
    /// emitted by the Instruction rewriters. They exist briefly while 
    /// the binary program is being scanned, and are then converted to
    /// IL code (<see cref="Reko.Core.Code.Instruction" />).
    /// </summary>
    public abstract class RtlInstruction
    {
        /// <summary>
        /// The class of this instruction.
        /// </summary>
        public InstrClass Class { get; set; }

        public abstract T Accept<T>(RtlInstructionVisitor<T> visitor);
        public abstract T Accept<T, C>(IRtlInstructionVisitor<T, C> visitor, C context);

        /// <summary>
        /// If true, the next statement needs a label. This is required in
        /// cases where the original machine code maps to many RtlInstructions,
        /// some of which are branches (see the X86 REP instruction for a
        /// particularly hideous example).
        /// </summary>
        public bool NextStatementRequiresLabel { get; set; }

        public override string ToString()
        {
            var sw = new StringWriter();
            Write(sw);
            return sw.ToString();
        }

        public void Write(TextWriter writer)
        {
            WriteInner(writer);
        }

        protected abstract void WriteInner(TextWriter writer);

        public static string FormatClass(InstrClass rtlClass)
        {
            var sb = new StringBuilder();
            switch (rtlClass & (InstrClass.Transfer | InstrClass.Linear | InstrClass.Return  | InstrClass.Terminates | InstrClass.Privileged))
            {
            case InstrClass.Linear:
                sb.Append((rtlClass & InstrClass.Unlikely) != 0 ? 'U':'L'); break;
            case InstrClass.Transfer:
            case InstrClass.Transfer | InstrClass.Linear:
                sb.Append('T'); break;
            case InstrClass.Transfer | InstrClass.Return:
            case InstrClass.Transfer | InstrClass.Return | InstrClass.Privileged:
                sb.Append('R'); break;
            case InstrClass.Terminates:
            case InstrClass.Terminates | InstrClass.Privileged:
                sb.Append('H'); break;
            case InstrClass.Privileged:
            case InstrClass.Privileged | InstrClass.Linear:
            case InstrClass.Privileged | InstrClass.Transfer:
                sb.Append('S'); break;
            default: sb.Append('-'); break;
            }
            sb.Append((rtlClass & InstrClass.Delay) != 0 ? 'D' : '-');
            sb.Append((rtlClass & InstrClass.Annul) != 0 ? 'A' : '-');
            return sb.ToString();
        }
    }
}
