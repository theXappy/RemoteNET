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

namespace Reko.Core
{
    /// <summary>
    /// The provenance of a basic block or procedure is evidence of how it
    /// arrived to be deduced.
    /// </summary>
    public class Provenance
    {
        public ProvenanceType ProvenanceType;
        public string? Comment;
    }

    public enum ProvenanceType
    {
        None,
        Image,              // reached here because image file "said so".
        ImageEntrypoint,    // An image defined this as the entry point.
        Environment,        // This object is dictated by the environment/platform.
        UserInput,          // reached here because user's input "said so".
        Scanning,           // reached here as part of the scanning process.
        Heuristic,          // reached here as a guess.
    }

    public class Provenance<T> : Provenance
    {
        public readonly T ReachedFrom;

        public Provenance(T from)
        {
            this.ReachedFrom = from;
        }
    }
}
