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

namespace Reko.Core.Operators
{
    public enum OperatorType
    {
        IAdd,
        ISub,
        USub,
        IMul,
        SMul,
        UMul,
        SDiv,
        UDiv,
        IMod,
        SMod,
        UMod,

        FAdd,
        FSub,
        FMul,
        FDiv,
        FMod,
        FNeg,

        And,
        Or,
        Xor,
        
        Shr,
        Sar,
        Shl,

        Cand,
        Cor,

        Lt,
        Gt,
        Le,
        Ge,

        Feq,
        Fne,
        Flt,
        Fgt,
        Fle,
        Fge,

        Ult,
        Ugt,
        Ule,
        Uge,

        Eq,
        Ne,

        Not,
        Neg,
        Comp,
        AddrOf,

        Comma,
    }

    public static class OperatorTypeExtensions
    {
        /// <summary>
        /// Returns whether the <see cref="OperatorType"/> is an integer
        /// addition or subtraction.
        /// </summary>
        public static bool IsAddOrSub(this OperatorType self)
        {
            return self == OperatorType.IAdd || self == OperatorType.ISub;
        }

        public static bool IsIntMultiplication(this OperatorType self)
        {
            return
               self == OperatorType.IMul ||
               self == OperatorType.SMul ||
               self == OperatorType.UMul;
        }

        public static bool IsShift(this OperatorType self)
        {
            return self == OperatorType.Shl || self == OperatorType.Shr || self == OperatorType.Sar;
        }
    }
}
