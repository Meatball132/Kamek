﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kamek
{
    class KamekFile
    {
        public static byte[] PackFrom(Linker linker)
        {
            var kf = new KamekFile();
            kf.LoadFromLinker(linker);
            return kf.Pack();
        }



        private Word _baseAddress;
        private byte[] _codeBlob;
        private long _bssSize;

        public Word BaseAddress { get { return _baseAddress; } }
        public byte[] CodeBlob { get { return _codeBlob; } }

        #region Result Binary Manipulation
        public ushort ReadUInt16(Word addr)
        {
            return Util.ExtractUInt16(_codeBlob, addr - _baseAddress);
        }
        public uint ReadUInt32(Word addr)
        {
            return Util.ExtractUInt32(_codeBlob, addr - _baseAddress);
        }
        public void WriteUInt16(Word addr, ushort value)
        {
            Util.InjectUInt16(_codeBlob, addr - _baseAddress, value);
        }
        public void WriteUInt32(Word addr, uint value)
        {
            Util.InjectUInt32(_codeBlob, addr - _baseAddress, value);
        }
        #endregion

        private Dictionary<Word, Commands.Command> _commands;
        private List<Hooks.Hook> _hooks;

        public void LoadFromLinker(Linker linker)
        {
            if (_codeBlob != null)
                throw new InvalidOperationException("this KamekFile already has stuff in it");

            // Extract _just_ the code/data sections
            _codeBlob = new byte[linker.OutputEnd - linker.OutputStart];
            Array.Copy(linker.Memory, linker.OutputStart - linker.BaseAddress, _codeBlob, 0, _codeBlob.Length);

            _baseAddress = linker.BaseAddress;
            _bssSize = linker.BssSize;

            _hooks = new List<Hooks.Hook>();
            _commands = new Dictionary<Word, Commands.Command>();

            AddRelocsAsCommands(linker.Fixups);
            foreach (var cmd in linker.Hooks)
                ApplyHook(cmd);
            ApplyStaticCommands();
        }


        private void AddRelocsAsCommands(IList<Linker.Fixup> relocs)
        {
            foreach (var rel in relocs)
            {
                if (_commands.ContainsKey(rel.source))
                    throw new InvalidOperationException(string.Format("duplicate commands for address {0}", rel.source));
                _commands[rel.source] = new Commands.RelocCommand(rel.source, rel.dest, rel.type);
            }
        }


        private void ApplyHook(Linker.HookData hookData)
        {
            var hook = Hooks.Hook.Create(hookData);
            foreach (var cmd in hook.Commands)
            {
                if (_commands.ContainsKey(cmd.Address))
                    throw new InvalidOperationException(string.Format("duplicate commands for address {0}", cmd.Address));
                _commands[cmd.Address] = cmd;
            }
            _hooks.Add(hook);
        }


        private void ApplyStaticCommands()
        {
            // leave _commands containing just the ones we couldn't apply here
            var original = _commands;
            _commands = new Dictionary<Word, Commands.Command>();

            foreach (var cmd in original.Values)
            {
                if (!cmd.Apply(this))
                    _commands[cmd.Address] = cmd;
            }
        }



        public byte[] Pack()
        {
            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms))
                {
                    bw.WriteBE((uint)0x4B616D65); // 'Kamek\0\0\1'
                    bw.WriteBE((uint)0x6B000001);
                    bw.WriteBE((uint)_bssSize);
                    bw.WriteBE((uint)_codeBlob.Length);

                    bw.Write(_codeBlob);
                }

                return ms.ToArray();
            }
        }

        public string PackRiivolution()
        {
            if (_baseAddress.Type == WordType.RelativeAddr)
                throw new InvalidOperationException("cannot pack a dynamically linked binary as a Riivolution patch");

            var elements = new List<string>();

            // add the big patch
            // (todo: valuefile support)
            var sb = new StringBuilder(_codeBlob.Length * 2);
            for (int i = 0; i < _codeBlob.Length; i++)
                sb.AppendFormat("{0:X2}", _codeBlob[i]);

            elements.Add(string.Format("<memory offset='0x{0:X8}' value='{1}' />", _baseAddress.Value, sb.ToString()));

            // add individual patches
            foreach (var pair in _commands)
                elements.Add(pair.Value.PackForRiivolution());

            return string.Join("\n", elements);
        }

        public string PackGeckoCodes()
        {
            if (_baseAddress.Type == WordType.RelativeAddr)
                throw new InvalidOperationException("cannot pack a dynamically linked binary as a Riivolution patch");

            var codes = new List<ulong>();

            // add the big patch
            long paddingSize = 0;
            if ((_codeBlob.Length % 8) != 0)
                paddingSize = 8 - (_codeBlob.Length % 8);

            ulong header = 0x06000000UL << 32;
            header |= (ulong)(_baseAddress.Value & 0x1FFFFFF) << 32;
            header |= (ulong)(_codeBlob.Length + paddingSize) & 0xFFFFFFFF;
            codes.Add(header);

            for (int i = 0; i < _codeBlob.Length; i += 8)
            {
                ulong bits = 0;
                if (i < _codeBlob.Length) bits |= (ulong)_codeBlob[i] << 56;
                if ((i + 1) < _codeBlob.Length) bits |= (ulong)_codeBlob[i + 1] << 48;
                if ((i + 2) < _codeBlob.Length) bits |= (ulong)_codeBlob[i + 2] << 40;
                if ((i + 3) < _codeBlob.Length) bits |= (ulong)_codeBlob[i + 3] << 32;
                if ((i + 4) < _codeBlob.Length) bits |= (ulong)_codeBlob[i + 4] << 24;
                if ((i + 5) < _codeBlob.Length) bits |= (ulong)_codeBlob[i + 5] << 16;
                if ((i + 6) < _codeBlob.Length) bits |= (ulong)_codeBlob[i + 6] << 8;
                if ((i + 7) < _codeBlob.Length) bits |= (ulong)_codeBlob[i + 7];
                codes.Add(bits);
            }

            // add individual patches
            foreach (var pair in _commands)
                codes.AddRange(pair.Value.PackGeckoCodes());

            // convert everything
            var elements = new string[codes.Count];
            for (int i = 0; i < codes.Count; i++)
                elements[i] = string.Format("{0:X8} {1:X8}", codes[i] >> 32, codes[i] & 0xFFFFFFFF);

            return string.Join("\n", elements);
        }
    }
}
