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

#pragma warning disable IDE1006

using Reko.Core.Expressions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Reko.Core
{
    /// <summary>
    /// An EvaluationContext is used by the SymbolicEvaluator to provide a 
    /// statement context for the evaluation.
    /// </summary>
    /// <remarks>
    /// For instance, it might be interesting to find the expression currently
    /// bound to an identifier, to see if a simplification could be made. The
    /// statements
    /// <code>
    ///     a = constant
    ///     b = a + 3
    /// </code>
    /// can be merged to
    /// <code>
    ///     b = (constant + 3)
    /// </code>
    /// if we know that a == constant.
    /// </remarks>
    public interface EvaluationContext
    {
        EndianServices Endianness { get; }
        int MemoryGranularity { get; }

        /// <summary>
        /// Gets the symbolic value of the identifier <paramref name="id"/>.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Expression? GetValue(Identifier id);

        /// <summary>
        /// Retrieves the value located at the address obtained by evaluating
        /// the <paramref name="access"/> expression.
        /// </summary>
        /// <param name="access">Memory access expression.</param>
        /// <param name="segmentMap">The segment map of the program
        /// being analyzed.</param>
        /// <returns>An expression at the memory address, or an instance
        /// of <see cref="InvalidConstant"/> if the address couldn't be resolved.
        /// </returns>
        Expression GetValue(MemoryAccess access, IReadOnlySegmentMap segmentMap);
        Expression GetValue(SegmentedAccess access, IReadOnlySegmentMap segmentMap);
        Expression GetValue(Application appl);
        Expression? GetDefiningExpression(Identifier id);

        /// <summary>
        /// Given an identifier <paramref name="id"/>, finds all the reaching
        /// definitions of id. If the operation is not supported, returns an
        /// empty list.
        /// </summary>
        /// <remarks>
        /// The intent is to traverse backward from uses to definitions and
        /// locate all reaching definitions. This can only be done efficiently
        /// on the SSA graph, so non-SSA implementations of ths interface will
        /// just return an empty list. On the SSA graph, we compute the
        /// closure by following the sources of copy assignments and the
        /// arguments of phi assignments.
        /// </remarks>
        /// <param name="id">Identifier whose closure we want to obtain.</param>
        /// <returns>A list of statements whose definitions reach the
        /// identifier's use.
        /// </returns>
        List<Statement> GetDefiningStatementClosure(Identifier id);
        void RemoveIdentifierUse(Identifier id);
        void UseExpression(Expression expr);
        void RemoveExpressionUse(Expression expr);
        void SetValue(Identifier id, Expression value);
        void SetValueEa(Expression ea, Expression value);
        void SetValueEa(Expression basePointer, Expression ea, Expression value);

        bool IsUsedInPhi(Identifier id);
        Expression MakeSegmentedAddress(Constant c1, Constant c2);

        /// <summary>
        /// Reinterprets a string of raw bits as a floating point number appropriate
        /// for the current architecture.
        /// </summary>
        /// <param name="rawBits">Raw bits to be interpreted.</param>
        /// <returns></returns>
        Constant ReinterpretAsFloat(Constant rawBits);
    }
}
