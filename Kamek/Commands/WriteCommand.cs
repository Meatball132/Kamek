﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kamek.Commands
{
    class WriteCommand : Command
    {
        public enum Type
        {
            Pointer = 1,
            Value32 = 2,
            Value16 = 3,
            Value8 = 4
        }

        private static Ids IdFromType(Type type, bool isConditional)
        {
            if (isConditional)
            {
                switch (type)
                {
                    case Type.Pointer: return Ids.CondWritePointer;
                    case Type.Value32: return Ids.CondWrite32;
                    case Type.Value16: return Ids.CondWrite16;
                    case Type.Value8: return Ids.CondWrite8;
                }
            }
            else
            {
                switch (type)
                {
                    case Type.Pointer: return Ids.WritePointer;
                    case Type.Value32: return Ids.Write32;
                    case Type.Value16: return Ids.Write16;
                    case Type.Value8: return Ids.Write8;
                }
            }

            throw new NotImplementedException();
        }



        public readonly Type ValueType;
        public readonly Word Value;
        public readonly Word? Original;

        public WriteCommand(Word address, Word value, Type valueType, Word? original)
            : base(IdFromType(valueType, original.HasValue), address)
        {
            Value = value;
            ValueType = valueType;
            Original = original;
        }

        public override byte[] PackArguments()
        {
            throw new NotImplementedException();
        }

        public override string PackForRiivolution()
        {
            Address.AssertAbsolute();
            Value.AssertAbsolute();

            if (Original.HasValue)
            {
                Original.Value.AssertNotRelative();

                switch (ValueType)
                {
                    case Type.Value8: return string.Format("<memory offset='0x{0:X8}' value='{1:X2}' original='{2:X2}' />", Address.Value, Value.Value, Original.Value.Value);
                    case Type.Value16: return string.Format("<memory offset='0x{0:X8}' value='{1:X4}' original='{2:X4}' />", Address.Value, Value.Value, Original.Value.Value);
                    case Type.Value32:
                    case Type.Pointer: return string.Format("<memory offset='0x{0:X8}' value='{1:X8}' original='{2:X8}' />", Address.Value, Value.Value, Original.Value.Value);
                }
            }
            else
            {
                switch (ValueType)
                {
                    case Type.Value8: return string.Format("<memory offset='0x{0:X8}' value='{1:X2}' />", Address.Value, Value.Value);
                    case Type.Value16: return string.Format("<memory offset='0x{0:X8}' value='{1:X4}' />", Address.Value, Value.Value);
                    case Type.Value32:
                    case Type.Pointer: return string.Format("<memory offset='0x{0:X8}' value='{1:X8}' />", Address.Value, Value.Value);
                }
            }

            return null;
        }

        public override IEnumerable<ulong> PackGeckoCodes()
        {
            Address.AssertAbsolute();
            Value.AssertAbsolute();

            if (Original.HasValue)
                throw new NotImplementedException("conditional writes not yet supported for gecko");
            if (Address.Value >= 0x90000000)
                throw new NotImplementedException("MEM2 writes not yet supported for gecko");

            ulong code = ((ulong)(Address.Value & 0x1FFFFFF) << 32) | Value.Value;
            switch (ValueType)
            {
                case Type.Value16: code |= 0x2000000UL << 32; break;
                case Type.Value32:
                case Type.Pointer: code |= 0x4000000UL << 32; break;
            }

            return new ulong[1] { code };
        }

        public override bool Apply(KamekFile file)
        {
            return false;
        }
    }
}
