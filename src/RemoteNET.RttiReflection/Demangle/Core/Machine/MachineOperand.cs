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

using Reko.Core;
using Reko.Core.Expressions;
using Reko.Core.Machine;
using Reko.Core.Types;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Reko.Core.Machine
{
    /// <summary>
    /// Abstraction of a processor instruction operand.
    /// </summary>
	public interface MachineOperand
	{
        /// <summary>
        /// The data type of this MachineOperand.
        /// </summary>
        DataType Width { get; set; }

        /// <summary>
        /// Renders the operand as a string, according to the specified
        // <paramref name="options" />.
        string ToString(MachineInstructionRendererOptions options);

        /// <summary>
        /// Renders the operand to a <see cref="MachineInstructionRenderer" />,
        /// according to the specified <paramref name="options" />.
        public void Render(MachineInstructionRenderer renderer, MachineInstructionRendererOptions options);
    }

    public abstract class AbstractMachineOperand : MachineOperand
	{
        public DataType Width { get; set; }

		protected AbstractMachineOperand(DataType width)
		{
			this.Width = width;
		}

        public sealed override string ToString()
        {
            return ToString(MachineInstructionRendererOptions.Default);
        }

		public string ToString(MachineInstructionRendererOptions options)
		{
            var sr = new StringRenderer();
            Render(sr, options);
			return sr.ToString();
		}

        public void Render(MachineInstructionRenderer renderer, MachineInstructionRendererOptions options)
        {
            renderer.BeginOperand();
            DoRender(renderer, options);
            renderer.EndOperand();
        }

        protected abstract void DoRender(MachineInstructionRenderer renderer, MachineInstructionRendererOptions options);

        /// <summary>
        /// Converts a signed integer constant to string representatioon.
        /// </summary>
        /// <param name="forceSign">Should signed integers always be
        /// formatted with a leading sign character? Setting this to true
        /// allows chaining a sequence of numbers into an expression,
        /// like "+5+7-3+9". Setting this to false will format numbers like
        /// "5" and "-3" which is normal for standalone numbers.</param>
        /// <param name="format">Format string; allows injecting platform-
        /// specific characters before/between/after sign and value when
        /// printing an integer value as hex. {0} will be the sign character
        /// (if any) and {1} will be the absolute value.
        /// </param>
        public static string FormatSignedValue(Constant c, bool forceSign = true, string format = "{0}{1}")
        {
            string s = (forceSign ? "+" : "");
            int tmp = c.ToInt32();
            if (tmp < 0)
            {
                s = "-";
                tmp = -tmp;
            }
            return string.Format(format, s, tmp.ToString(FormatString(c.DataType)));
        }

        private static readonly char[] floatSpecials = new char[] { '.', 'e', 'E' };

        /// <summary>
        /// Converts a numeric constant to string representatioon.
        /// </summary>
        /// <param name="forceSignForSignedIntegers">sign Should signed
        /// integers always be formatted with a leading sign character?
        /// Setting this to true allows chaining a sequence of numbers into an
        /// expression,like "+5+7-3+9". Setting this to false will format
        /// numbers like "5" and "-3" which is normal for standalone numbers.
        /// </param>
        /// <param name="integerFormat">Format string; allows injecting 
        /// platform-specific characters before/between/after sign and
        /// value when printing an integer value as hex. {0} will be the sign
        /// character (if any) and {1} will be the absolute value.
        /// </param>
        public static string FormatValue(Constant c, bool forceSignForSignedIntegers = true, string integerFormat = "{0}{1}")
        {
            var pt = (PrimitiveType)c.DataType;
            if (pt.Domain == Domain.SignedInt)
            {
                return FormatSignedValue(c, forceSignForSignedIntegers, integerFormat);
            }
            else if (pt.Domain == Domain.Real)
            {
                var str = c.ToReal64().ToString("G", CultureInfo.InvariantCulture);
                if (str.IndexOfAny(floatSpecials) < 0)
                {
                    return str + ".0";
                }
                return str;
            }
            else
                return FormatUnsignedValue(c, integerFormat);
        }

		private static string FormatString(DataType dt)
		{
            if (dt.Size < 8)
                return $"X{dt.Size * 2}";
            else
                return "X8";
		}

		public static string FormatUnsignedValue(Constant c, string format = "{0}{1}")
		{
			return string.Format(format, "", c.ToUInt64().ToString(FormatString(c.DataType)));
		}
	}

    /// <summary>
    /// Represents an immediate constant value used by a MachineInstruction.
    /// </summary>
	public class ImmediateOperand : AbstractMachineOperand
    {
        public ImmediateOperand(Constant c) : base((PrimitiveType) c.DataType)
        {
            Value = c;
        }

        public Constant Value { get; }

        protected override void DoRender(MachineInstructionRenderer renderer, MachineInstructionRendererOptions options)
        {
            var s = FormatValue(Value);
            if (Value.DataType.Domain == Domain.Pointer)
                renderer.WriteAddress(s, Address.FromConstant(Value));
            else
                renderer.WriteString(s);
        }

        public static ImmediateOperand Create(Constant value)
        {
            return new ImmediateOperand(value);
        }

        public static ImmediateOperand Byte(byte value)
        {
            return new ImmediateOperand(Constant.Byte(value));
        }

        public static ImmediateOperand SByte(sbyte value)
        {
            return new ImmediateOperand(Constant.SByte(value));
        }

        public static ImmediateOperand UInt16(ushort value)
        {
            return new ImmediateOperand(Constant.UInt16(value));
        }

        public static ImmediateOperand UInt32(uint value)
        {
            return new ImmediateOperand(Constant.UInt32(value));
        }

        public static ImmediateOperand Word32(int value)
        {
            return new ImmediateOperand(Constant.Word32(value));
        }

        public static ImmediateOperand Word32(uint value) { return Word32((int)value); }

        public static ImmediateOperand Word64(ulong value) { return Word64((long) value); }

        public static ImmediateOperand Word64(long value)
        {
            return new ImmediateOperand(Constant.Word64(value));
        }

        public static ImmediateOperand Word128(ulong value)
        {
            return new ImmediateOperand(new BigConstant(PrimitiveType.Word128, value));
        }

        public static ImmediateOperand Int32(int value)
        {
            return new ImmediateOperand(Constant.Int32(value));
        }

        public static AbstractMachineOperand Int16(short value)
        {
            return new ImmediateOperand(Constant.Int16(value));
        }

        public static AbstractMachineOperand Word16(ushort value)
        {
            return new ImmediateOperand(Constant.Word16(value));
        }
    }

    /// <summary>
    /// Represents a machine address.
    /// </summary>
    public class AddressOperand : AbstractMachineOperand
    {
        public Address Address;

        protected AddressOperand(Address addr, PrimitiveType type)
            : base(type)
        {
            Address = addr ?? throw new ArgumentNullException(nameof(addr));
        }

        public static AddressOperand Create(Address addr)
        {
            return new AddressOperand(
                addr,
                PrimitiveType.Create(Domain.Pointer, addr.DataType.BitSize));
        }

        public static AddressOperand Ptr16(ushort a)
        {
            return new AddressOperand(Address.Ptr16(a), PrimitiveType.Ptr16);
        }

        public static AddressOperand Ptr32(uint a)
        {
            return new AddressOperand(Address.Ptr32(a), PrimitiveType.Ptr32);
        }

        public static AddressOperand Ptr64(ulong a)
        {
            return new AddressOperand(Address.Ptr64(a), PrimitiveType.Ptr64);
        }

        protected override void DoRender(MachineInstructionRenderer renderer, MachineInstructionRendererOptions options)
        {
            renderer.WriteAddress(Address.ToString(), Address);
        }
    }

    /// <summary>
    /// Represents a FPU operand.
    /// </summary>
	public class FpuOperand : AbstractMachineOperand
	{
		private readonly int fpuReg;

		public FpuOperand(int f) : base(PrimitiveType.Real64)
		{
			fpuReg = f;
		}

		public int StNumber
		{
			get { return fpuReg; }
		}

		protected override void DoRender(MachineInstructionRenderer renderer, MachineInstructionRendererOptions options)
        {
			renderer.WriteString("st(" + fpuReg + ")");
		}
	}
}


