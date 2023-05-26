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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Reko.Core.Collections
{
    /// <summary>
    /// An extension of <see cref="IEnumerator{T}" /> that wraps an IEnumerator and 
    /// which lets the caller peek ahead in the underlying enumeration.
    /// </summary>
    public class LookaheadEnumerator<T> : IEnumerator<T>
    {
        private readonly IEnumerator<T> e;
        private readonly List<T> peeked;
        private int iCur;

        public LookaheadEnumerator(IEnumerator<T> innerEnumerator)
        {
            this.e = innerEnumerator ?? throw new ArgumentNullException(nameof(innerEnumerator));
            this.peeked = new List<T>();
            this.iCur = 0;
        }

        public LookaheadEnumerator(IEnumerable<T> collection) : this(collection.GetEnumerator())
        {
        }

        #region IEnumerator<T> Members

        public T Current
        {
            get
            {
                if (!this.TryPeek(0, out var result))
                    throw new InvalidOperationException();
                return result!;
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            e.Dispose();
        }

        object System.Collections.IEnumerator.Current
        {
            get { return Current!; }
        }

        public bool MoveNext()
        {
            if (iCur < peeked.Count-1)
            {
                ++iCur;
                return true;
            }
            iCur = 0;
            peeked.Clear();
            return e.MoveNext();
        }

        public void Reset()
        {
            iCur = 0;
            peeked.Clear();
            e.Reset();
        }

        #endregion

        /// <summary>
        /// Attempt to peek <see cref="ahead"/> items forward in the enumeration.
        /// </summary>
        /// <param name="ahead">Number of steps ahead to peek.</param>
        /// <param name="result">If the peek was successful, the peeked
        /// item.</param>
        /// <returns>True if the peek was successful, false if not.</returns>
        public bool TryPeek(int ahead, [MaybeNullWhen(false)] out T? result)
        {
            if (ahead < 0)
                throw new ArgumentOutOfRangeException("Parameter must be non-negative.", nameof(ahead));
            int itemsInBuffer = peeked.Count - iCur;
            Debug.Assert(itemsInBuffer >= 0);
            if (itemsInBuffer == 0 && ahead == 0)
            {
                result = e.Current;
                return true;
            }
            if (ahead < itemsInBuffer)
            {
                result = peeked[iCur + ahead];
                return true;
            }
            if (itemsInBuffer == 0)
                peeked.Add(e.Current);
            for (int i = 0; i < ahead; ++i)
            {
                if (!e.MoveNext())
                {
                    result = default;
                    return false;
                }
                peeked.Add(e.Current);
            }
            result = peeked[ahead];
            return true;
        }

        public void Skip(int skip)
        {
            int itemsInBuffer = peeked.Count - iCur;
            if (skip < itemsInBuffer)
            {
                iCur += skip;
                return;
            }
        }
    }
}
