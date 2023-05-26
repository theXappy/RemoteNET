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

namespace Reko.Core.Absyn
{
	public class AbsynDeclaration : AbsynStatement
	{
		public AbsynDeclaration(Identifier id, Expression? expr)
		{
			this.Identifier = id;
			this.Expression = expr;
		}

        public Identifier Identifier { get; }
        public Expression? Expression { get; set; }
        
        public override void Accept(IAbsynVisitor visitor)
		{
			visitor.VisitDeclaration(this);
		}

        public override T Accept<T>(IAbsynVisitor<T> visitor)
        {
            return visitor.VisitDeclaration(this);
        }
	}
}
