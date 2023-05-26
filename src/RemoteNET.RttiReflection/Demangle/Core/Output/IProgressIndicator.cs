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

namespace Reko.Core.Output
{
    /// <summary>
    /// Interface to use when displaying progress during a 
    /// long operation.
    /// </summary>
    public interface IProgressIndicator
    {
        /// <summary>
        /// Update the main progress caption.
        /// </summary>
        void SetCaption(string newCaption);

        /// <summary>
        /// Update the sub-progress caption.
        /// </summary>
        void ShowStatus(string caption);

        /// <summary>
        /// Update the sub-progress caption and the progress made.
        /// </summary>
        void ShowProgress(string caption, int numerator, int denominator);

        /// <summary>
        /// Update only the progress made, possibly changing the total
        /// number of steps.
        /// </summary>
        void ShowProgress(int numerator, int denominator);

        /// <summary>
        /// Advance by a specific number of steps.
        /// </summary>
        /// <param name="count"></param>
        void Advance(int count);

        /// <summary>
        /// Signal the completion of the task.
        /// </summary>
        void Finish();
    }

    public class NullProgressIndicator : IProgressIndicator
    {
        public static NullProgressIndicator Instance { get; } = new NullProgressIndicator();

        private NullProgressIndicator()
        {
        }

        public void SetCaption(string newCaption)
        {
        }

        public void ShowStatus(string caption)
        {
        }

        public void ShowProgress(string caption, int numerator, int denominator)
        {
        }

        public void ShowProgress(int numerator, int denominator)
        {
        }

        public void Advance(int count)
        {
        }

        public void Finish()
        {
        }
    }
}
