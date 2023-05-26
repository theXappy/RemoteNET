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
using System.IO;
using System.Linq;

namespace Reko.Core
{
    /// <summary>
    /// Contains the non-global locations accessed by the code inside a procedure. 
    /// </summary>
    /// <remarks>
    /// Variables accessed by the procedure can live in many places: registers, stack, and temporaries.
    /// <para>
    /// The frame layout is particularly interesting. On the Intel x86 architecture in real mode we have the following:
    /// </para>
    /// <code>
    ///   Layout             offset value
    /// +-----------------+
    /// | arg3            |   6
    /// +-----------------+
    /// | arg2            |   4
    /// +-----------------+
    /// | arg1            |   2
    /// +-----------------+
    /// | return address  |	  0
    /// +-----------------+
    /// | frame pointer   |   -2
    /// +-----------------+
    /// | local1          |   -4
    /// +-----------------+
    /// | local2          |   -6
    /// 
    /// etc.
    /// </code>
    /// <para>Note that variables that are stack arguments use offsets based on the state of stack
    /// _after_ the return address was pushed. The return address, if passed on the stack, is always
    /// considered to be at offset 0.</para>
    /// <para>In addition, support has to be provided for architectures that have separate FPU stacks.</para>
    /// </remarks>
    public class Frame : IStorageBinder
	{
        private readonly IProcessorArchitecture arch;
		private readonly List<Identifier> identifiers;	// Identifiers for each access.
        private readonly NamingPolicy namingPolicy;
		
        /// <summary>
        /// Creates a Frame instance for maintaining the local variables and arguments.
        /// </summary>
        /// <param name="framePointerSize">The size of the frame pointer must match the size of the 
        /// stack register, if any, or at the least the size of a pointer.</param>
        /// <param name="codeAddressSize">The size of the return address. Usually it is the same
        /// as a pointer size, but on x86 this can differ from the frame pointer size.</param>
		public Frame(
            IProcessorArchitecture arch,
            PrimitiveType framePointerSize,
            PrimitiveType codeAddressSize)
		{
            if (framePointerSize is null)
                throw new ArgumentNullException(nameof(framePointerSize));
            this.arch = arch;
			identifiers = new List<Identifier>();
            this.namingPolicy = NamingPolicy.Instance;

			// There is always a "variable" for the global memory and the frame
			// pointer.

            this.Memory = new MemoryIdentifier(0, new UnknownType());
            this.Identifiers.Add(Memory);
			this.FramePointer = CreateTemporary("fp", framePointerSize);
            this.Continuation = CreateTemporary("%continuation", codeAddressSize);
		}

        public Frame(IProcessorArchitecture arch, PrimitiveType pointerSize) : this(arch, pointerSize, pointerSize)
        {
        }

        public bool Escapes { get; set; }
        public int FrameOffset { get; set; }

        /// <summary>
        /// A virtual register that is a constant pointer to 
        /// </summary>
        public Identifier FramePointer { get; private set; }
        public Identifier Continuation { get; private set; }
        public List<Identifier> Identifiers { get { return identifiers; } }
        public Identifier Memory { get; private set; }

        /// <summary>
        /// Amount of bytes that the calling function pushed on the stack to store 
        /// the return address. Some architectures pass the return address in a register,
        /// which implies that in those architectures the return address size should be zero.
        /// </summary>
        public int ReturnAddressSize { get; set; }
        public bool ReturnAddressKnown { get; set; }

        public Identifier CreateSequence(DataType dt, params Storage [] elements)
        {
            var name = string.Join("_", elements.Select(e => e.Name));
            var id = new Identifier(name, dt, new SequenceStorage(dt, elements));
            identifiers.Add(id);
            return id;
        }

        public Identifier CreateSequence(DataType dt, string name, params Storage[] elements)
        {
            var id = new Identifier(name, dt, new SequenceStorage(dt, elements));
            identifiers.Add(id);
            return id;
        }

        public Identifier EnsureIdentifier(Storage stgForeign)
        {
            switch (stgForeign)
            {
            case RegisterStorage reg:
                return EnsureRegister(reg);
            case FlagGroupStorage grf:
                return EnsureFlagGroup(grf);
            case SequenceStorage seq:
                return EnsureSequence(seq);
            case FpuStackStorage fp:
                return EnsureFpuStackVariable(fp.FpuStackOffset, fp.DataType);
            case StackStorage st:
                return EnsureStackVariable(st.StackOffset, st.DataType);
            case TemporaryStorage tmp:
                return CreateTemporary(tmp.Name, tmp.DataType);
            }
            throw new NotImplementedException(string.Format(
                "Unsupported storage {0}.",
                stgForeign != null ? stgForeign.ToString() : "(null)"));
        }

		/// <summary>
		/// Creates a temporary variable whose storage and name is guaranteed not to collide with any other variable.
		/// </summary>
		/// <param name="dt"></param>
		/// <returns></returns>
		public Identifier CreateTemporary(DataType dt)
		{
            string name = "v" + identifiers.Count;
			var id = new Identifier(name, dt,
                new TemporaryStorage(name, identifiers.Count, dt));
			identifiers.Add(id);
			return id;
		}

		public Identifier CreateTemporary(string name, DataType dt)
		{
            var id = new Identifier(name, dt, 
                new TemporaryStorage(name, identifiers.Count, dt));
			identifiers.Add(id);
			return id;
		}

        public Identifier EnsureFlagGroup(RegisterStorage freg, uint grfMask, string name, DataType dt)
		{
            if (grfMask == 0)
                throw new ArgumentException("Argument must be non-zero.", nameof(grfMask));
			Identifier? id = FindFlagGroup(freg, grfMask);
			if (id is null)
			{
				id = Identifier.Create(new FlagGroupStorage(freg, grfMask, name, dt));
				identifiers.Add(id);
			}
			return id;
		}

        public Identifier EnsureFlagGroup(FlagGroupStorage grf)
        {
            if (grf.FlagGroupBits == 0)
                throw new ArgumentException("Argument must have non-zero flag group bits.", nameof(grf));
            var id = FindFlagGroup(grf.FlagRegister, grf.FlagGroupBits);
            if (id is null)
            {
                id = Identifier.Create(new FlagGroupStorage(grf.FlagRegister, grf.FlagGroupBits, grf.Name, grf.DataType));
                identifiers.Add(id);
            }
            return id;
        }

		public Identifier EnsureFpuStackVariable(int depth, DataType type)
		{
			Identifier? id = FindFpuStackVariable(depth);
			if (id is null)
			{
				string name = string.Format("{0}{1}", (depth < 0 ? "rLoc" : "rArg"), Math.Abs(depth));
				id = new Identifier(name, type, new FpuStackStorage(depth, type));
				identifiers.Add(id);
			}
			return id;
		}

		/// <summary>
		/// Ensures a register access in this function. 
		/// </summary>
		/// <param name="reg"></param>
		/// <param name="name"></param>
		/// <param name="vt"></param>
		/// <returns></returns>
		public Identifier EnsureRegister(RegisterStorage reg)
		{
			Identifier? id = FindRegister(reg);
			if (id is null)
			{
                id = Identifier.Create(reg);
				identifiers.Add(id);
			}
			return id;
		}

		public Identifier EnsureOutArgument(Identifier idOrig, DataType outArgumentPointer)
		{
			Identifier? idOut = FindOutArgument(idOrig);
			if (idOut is null)
			{
				idOut = new Identifier(idOrig.Name + "Out", outArgumentPointer, new OutArgumentStorage(idOrig));
				identifiers.Add(idOut);
			}
			return idOut;
		}

        public Identifier EnsureSequence(SequenceStorage sequence)
        {
            var idSeq = FindSequence(sequence.Elements);
            if (idSeq is null)
            {
                idSeq = new Identifier(sequence.Name, sequence.Width, sequence);
                identifiers.Add(idSeq);
            }
            return idSeq;
        }

		public Identifier EnsureSequence(DataType dt, params Storage [] elements)
        {
			Identifier? idSeq = FindSequence(elements);
			if (idSeq is null)
			{
				idSeq = CreateSequence(dt, elements);
			}
			return idSeq;
		}

        public Identifier EnsureSequence(DataType dt, string name, params Storage [] elements)
        {
            Identifier? idSeq = FindSequence(elements);
            if (idSeq == null)
            {
                idSeq = CreateSequence(dt, name, elements);
            }
            return idSeq;
        }

        /// <summary>
        /// Makes sure that there is a local variable at the given offset.
        /// </summary>
        /// <param name="cbOffset"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public Identifier EnsureStackLocal(int cbOffset, DataType type)
		{
			return EnsureStackLocal(cbOffset, type, null);
		}

		public Identifier EnsureStackLocal(int cbOffset, DataType type, string? name)
		{
			Identifier? id = FindStackLocal(cbOffset, type);
			if (id == null)
			{
				id = new Identifier(namingPolicy.StackLocalName(type, cbOffset, name), type, new StackStorage(cbOffset, type));
				identifiers.Add(id);
			}
			return id;
		}

		public Identifier EnsureStackArgument(int cbOffset, DataType type)
		{
			return EnsureStackArgument(cbOffset, type, null);
		}

		public Identifier EnsureStackArgument(int cbOffset, DataType type, string? argName)
		{
			Identifier? id = FindStackArgument(cbOffset, type.Size);
			if (id == null)
			{
				id = new Identifier(
					namingPolicy.StackArgumentName(type, cbOffset, argName), 
					type, 
					new StackStorage(cbOffset, type));
				identifiers.Add(id);
			}			
			return id;
		}

		public Identifier EnsureStackVariable(Constant imm, int cbOffset, DataType type)
		{
			if (imm != null && imm.IsValid)
			{
				cbOffset = imm.ToInt32() - cbOffset;
			}
			else
				cbOffset = -cbOffset;
			return (cbOffset >= 0)
				? EnsureStackArgument(cbOffset, type)
				: EnsureStackLocal(cbOffset, type);
		}

        public Identifier EnsureStackVariable(int byteOffset, DataType type)
        {
            return arch.IsStackArgumentOffset(byteOffset)
                ? EnsureStackArgument(byteOffset, type)
                : EnsureStackLocal(byteOffset, type);
        }

        public Identifier? FindSequence(Storage[] elements)
        {
            foreach (Identifier id in identifiers)
            {
                if (id.Storage is SequenceStorage seq &&
                    seq.Elements.Length == elements.Length)
                {
                    var allSame = true;
                    for (int i = 0; allSame && i < seq.Elements.Length; ++i)
                    {
                        allSame &= seq.Elements[i].Equals(elements[i]);
                    }
                    if (allSame)
                        return id;
                }
            }
            return null;
        }

		public Identifier? FindFlagGroup(RegisterStorage reg, uint grfMask)
		{
			foreach (Identifier id in identifiers)
			{
                if (id.Storage is FlagGroupStorage flags &&
                    flags.FlagRegister == reg &&
                    flags.FlagGroupBits == grfMask)
                {
                    return id;
                }
            }
			return null;
		}

		public Identifier? FindFpuStackVariable(int off)
		{
			foreach (Identifier id in identifiers)
			{
                if (id.Storage is FpuStackStorage fst && fst.FpuStackOffset == off)
                    return id;
            }
			return null;
		}

		public Identifier? FindOutArgument(Identifier idOrig)
		{
			foreach (Identifier id in identifiers)
			{
                if (id.Storage is OutArgumentStorage s && s.OriginalIdentifier == idOrig)
                {
                    return id;
                }
            }
			return null;
		}

		public Identifier? FindRegister(RegisterStorage reg)
		{
			foreach (Identifier id in identifiers)
			{
                if (id.Storage is RegisterStorage s && s == reg)
                    return id;
            }
			return null;
		}

		public Identifier? FindStackArgument(int offset, int size)
		{
			foreach (Identifier id in identifiers)
			{
                if (id.Storage is StackStorage s && s.StackOffset == offset && id.DataType.Size == size)
                {
                    return id;
                }
            }
			return null;
		}

		public Identifier? FindStackLocal(int offset, DataType dt)
		{
			foreach (Identifier id in identifiers)
			{
                if (id.Storage is StackStorage loc &&
                    loc.StackOffset == offset &&
                    (id.DataType.Size == dt.Size))
                {
                    return id;
                }
            }
			return null;
		}

        public Identifier? FindTemporary(string name)
        {
            return (from id in identifiers
                   let tmp = id.Storage as TemporaryStorage
                   where tmp != null && id.Name == name
                   select id).SingleOrDefault();
        }

		public void Write(TextWriter text)
		{
			foreach (Identifier id in identifiers)
			{
				text.Write("// ");
				text.Write(id.Name);
				text.Write(":");
				id.Storage.Write(text);
				text.WriteLine();
			}
			if (Escapes)
				text.WriteLine("// Frame escapes");
			text.WriteLine("// return address size: {0}", ReturnAddressSize);
		}
    }
}