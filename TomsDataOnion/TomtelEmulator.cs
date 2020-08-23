using System;
using System.Collections.Generic;
using System.IO;

namespace TomsDataOnion
{
  public enum Regs8 { a, b, c, d, e, f };
  public enum Regs32 { la, lb, lc, ld, ptr, pc };
  public enum Instructions { ADD, APTR, CMP, HALT, JEZ, JNZ, MV, MV32, MVI, MVI32, OUT, SUB, XOR };

  public class TomtelEmulator
  {
    private const byte PREFIX_ANY_MV = 0b11000000;
    private const byte PREFIX_MV_MVI = 0b01000000;
    private const byte PREFIX_MV32_MVI32 = 0b10000000;

    private byte[] Reg8 = new byte[6];
    private uint[] Reg32 = new uint[6];
    private byte[] Mem { get; }
    private Stream Output { get; }
    private TextWriter Debug { get; }

    private byte MemReg
    {
      get
      {
        return this.Mem[this.Reg32[(byte)Regs32.ptr] + this.Reg8[(byte)Regs8.c]];
      }
      set
      {
        this.Mem[this.Reg32[(byte)Regs32.ptr] + this.Reg8[(byte)Regs8.c]] = value;
      }
    }

    private Dictionary<Instructions, byte> InstructionSize = new Dictionary<Instructions, byte>
    {
      { Instructions.ADD,   1 },
      { Instructions.APTR,  2 },
      { Instructions.CMP,   1 },
      { Instructions.HALT,  1 },
      { Instructions.JEZ,   5 },
      { Instructions.JNZ,   5 },
      { Instructions.MV,    1 },
      { Instructions.MV32,  1 },
      { Instructions.MVI,   2 },
      { Instructions.MVI32, 5 },
      { Instructions.OUT,   1 },
      { Instructions.SUB,   1 },
      { Instructions.XOR,   1 },
    };

    private Dictionary<byte, Instructions> InstructionCode = new Dictionary<byte, Instructions>
    {
      { 0xC2, Instructions.ADD },
      { 0xE1, Instructions.APTR },
      { 0xC1, Instructions.CMP },
      { 0x01, Instructions.HALT },
      { 0x21, Instructions.JEZ },
      { 0x22, Instructions.JNZ },

      { 0b01000001, Instructions.MV },
      { 0b10000001, Instructions.MV32 },
      { 0b01000000, Instructions.MVI },
      { 0b10000000, Instructions.MVI32 },

      { 0x02, Instructions.OUT },
      { 0xC3, Instructions.SUB },
      { 0xC4, Instructions.XOR },
    };

    public TomtelEmulator(byte[] mem, Stream output, TextWriter debug = null)
    {
      this.Mem = mem;
      this.Output = output;
      this.Debug = debug;
    }

    public void Run(uint timeoutMs = 10000)
    {
      var startTs = Environment.TickCount64;

      Console.WriteLine("Starting byecode execution...");

      // When running, it does the following in a loop
      // 1.Reads one instruction from memory, at the address stored in the `pc` register
      // 2.Adds the byte size of the instruction to the `pc` register.
      // 3.Executes the instruction.
      var finished = false;
      while (!finished && Environment.TickCount64 < startTs + timeoutMs)
      {
        var addr = this.ReadInstruction();
        finished = this.Execute(addr);
      }
      Console.WriteLine(finished ? "Execution completed." : "Execution timed out.");
    }

    private uint ReadInstruction()
    {
      var addr = this.Reg32[(byte)Regs32.pc];
      var size = GetInstructionSize(addr);
      this.Reg32[(byte)Regs32.pc] += (uint)size;
      return addr;
    }

    private int GetInstructionSize(uint addr)
    {
      var operation = this.Mem[addr];
      return this.InstructionSize[this.GetInstructionCode(operation)];
    }

    private Instructions GetInstructionCode(byte b)
    {
      var x = b;
      if (((b & PREFIX_ANY_MV) == PREFIX_MV_MVI) || ((b & PREFIX_ANY_MV) == PREFIX_MV32_MVI32))
      {
        x = (byte)((b & PREFIX_ANY_MV) + (((b & 0b111) == 0) ? 0 : 1)); // if src == 0, it's immediate
      }

      if (!this.InstructionCode.ContainsKey(x))
        throw new InvalidOperationException($"Unsupported instruction code {b:X}");

      return this.InstructionCode[x];
    }

    private bool Execute(uint addr)
    {
      var code = this.GetInstructionCode(this.Mem[addr]);

      // For MV* instructions - 0b01DDDSSS
      var src = this.Mem[addr] & 0b111;
      var dest = (this.Mem[addr] >> 3) & 0b111;

      switch (code)
      {
        /*
          --[ ADD a <- b ]--------------------------------------------

            8-bit addition
            Opcode: 0xC2 (1 byte)

            Sets `a` to the sum of `a` and `b`, modulo 256.
        */
        case Instructions.ADD:
          this.Debug?.WriteLine($"ADD   a <- {this.Reg8[(byte)Regs8.a]:X} + {this.Reg8[(byte)Regs8.b]:X} = {(byte)(this.Reg8[(byte)Regs8.a] + this.Reg8[(byte)Regs8.b]):X}");
          this.Reg8[(byte)Regs8.a] += this.Reg8[(byte)Regs8.b];
          break;

        /*
          --[ APTR imm8 ]---------------------------------------------

            Advance ptr
            Opcode: 0xE1 0x__ (2 bytes)

            Sets `ptr` to the sum of `ptr` and `imm8`. Overflow
            behaviour is undefined.
        */
        case Instructions.APTR:
          this.Debug?.WriteLine($"APTR  ptr <- {this.Reg32[(byte)Regs32.ptr]:X} + {this.Mem[addr + 1]:X} = {this.Reg32[(byte)Regs32.ptr] + this.Mem[addr + 1]:X}");
          this.Reg32[(byte)Regs32.ptr] += this.Mem[addr + 1];
          break;

        /*
          --[ CMP ]---------------------------------------------------

            Compare
            Opcode: 0xC1 (1 byte)

            Sets `f` to zero if `a` and `b` are equal, otherwise sets
            `f` to 0x01.
        */
        case Instructions.CMP:
          this.Debug?.WriteLine($"CMP   f <- a({this.Reg8[(byte)Regs8.a]:X})==b({this.Reg8[(byte)Regs8.b]:X}) = {(this.Reg8[(byte)Regs8.a] == this.Reg8[(byte)Regs8.b] ? (byte)0 : (byte)1):X}");
          this.Reg8[(byte)Regs8.f] = (this.Reg8[(byte)Regs8.a] == this.Reg8[(byte)Regs8.b] ? (byte)0 : (byte)1);
          break;

        /*
          --[ HALT ]--------------------------------------------------

            Halt execution
            Opcode: 0x01 (1 byte)

            Stops the execution of the virtual machine. Indicates that
            the program has finished successfully.
        */
        case Instructions.HALT:
          this.Debug?.WriteLine($"HALT");
          return true;

        /*
          --[ JEZ imm32 ]---------------------------------------------

            Jump if equals zero
            Opcode: 0x21 0x__ 0x__ 0x__ 0x__ (5 bytes)

            If `f` is equal to zero, sets `pc` to `imm32`. Otherwise
            does nothing.
        */
        case Instructions.JEZ:
          this.Debug?.WriteLine($"JEZ   f={this.Reg8[(byte)Regs8.f]:X} addr={this.ReadUint(addr + 1):X}");
          if (this.Reg8[(byte)Regs8.f] == 0)
            this.Reg32[(byte)Regs32.pc] = this.ReadUint(addr + 1);
          break;

        /*
          --[ JNZ imm32 ]---------------------------------------------

            Jump if not zero
            Opcode: 0x22 0x__ 0x__ 0x__ 0x__ (5 bytes)

            If `f` is not equal to zero, sets `pc` to `imm32`.
            Otherwise does nothing.
        */
        case Instructions.JNZ:
          this.Debug?.WriteLine($"JNZ   f={this.Reg8[(byte)Regs8.f]:X} addr={this.ReadUint(addr + 1):X}");
          if (this.Reg8[(byte)Regs8.f] != 0)
            this.Reg32[(byte)Regs32.pc] = this.ReadUint(addr + 1);
          break;

        /*
          --[ MV {dest} <- {src} ]------------------------------------

            Move 8-bit value
            Opcode: 0b01DDDSSS (1 byte)

            Sets `{dest}` to the value of `{src}`.

            Both `{dest}` and `{src}` are 3-bit unsigned integers that
            correspond to an 8-bit register or pseudo-register. In the
            opcode format above, the "DDD" bits are `{dest}`, and the
            "SSS" bits are `{src}`. Below are the possible valid
            values (in decimal) and their meaning.

                                    1 => `a`
                                    2 => `b`
                                    3 => `c`
                                    4 => `d`
                                    5 => `e`
                                    6 => `f`
                                    7 => `(ptr+c)`

            A zero `{src}` indicates an MVI instruction, not MV.
        */
        case Instructions.MV:
          var val = (src == 7 ? this.MemReg : this.Reg8[src - 1]);
          this.Debug?.WriteLine($"MV    {(dest == 7 ? "(ptr+c)" : ((char)('a' + dest - 1)).ToString())} <- {(src == 7 ? "(ptr+c)" : ((char)('a' + src - 1)).ToString())}={val:X}");

          if (dest == 7)
            this.MemReg = val;
          else
            this.Reg8[dest - 1] = val;
          break;

        /*
          --[ MV32 {dest} <- {src} ]----------------------------------

            Move 32-bit value
            Opcode: 0b10DDDSSS (1 byte)

            Sets `{dest}` to the value of `{src}`.

            Both `{dest}` and `{src}` are 3-bit unsigned integers that
            correspond to a 32-bit register. In the opcode format
            above, the "DDD" bits are `{dest}`, and the "SSS" bits are
            `{src}`. Below are the possible valid values (in decimal)
            and their meaning.

                                    1 => `la`
                                    2 => `lb`
                                    3 => `lc`
                                    4 => `ld`
                                    5 => `ptr`
                                    6 => `pc`
        */
        case Instructions.MV32:
          this.Reg32[dest - 1] = this.Reg32[src - 1];
          break;

        /*
          --[ MVI {dest} <- imm8 ]------------------------------------

            Move immediate 8-bit value
            Opcode: 0b01DDD000 0x__ (2 bytes)

            Sets `{dest}` to the value of `imm8`.

            `{dest}` is a 3-bit unsigned integer that corresponds to
            an 8-bit register or pseudo-register. It is the "DDD" bits
            in the opcode format above. Below are the possible valid
            values (in decimal) and their meaning.

                                    1 => `a`
                                    2 => `b`
                                    3 => `c`
                                    4 => `d`
                                    5 => `e`
                                    6 => `f`
                                    7 => `(ptr+c)`
        */
        case Instructions.MVI:
          this.Debug?.WriteLine($"MVI   {(dest == 7 ? "(ptr+c)" : ((char)('a' + dest - 1)).ToString())} <- {this.Mem[addr + 1]:X}");
          if (dest == 7)
            this.MemReg = this.Mem[addr + 1];
          else
            this.Reg8[dest - 1] = this.Mem[addr + 1];
          break;

        /*
          --[ MVI32 {dest} <- imm32 ]---------------------------------

            Move immediate 32-bit value
            Opcode: 0b10DDD000 0x__ 0x__ 0x__ 0x__ (5 bytes)

            Sets `{dest}` to the value of `imm32`.

            `{dest}` is a 3-bit unsigned integer that corresponds to a
            32-bit register. It is the "DDD" bits in the opcode format
            above. Below are the possible valid values (in decimal)
            and their meaning.

                                    1 => `la`
                                    2 => `lb`
                                    3 => `lc`
                                    4 => `ld`
                                    5 => `ptr`
                                    6 => `pc`
        */
        case Instructions.MVI32:
          this.Reg32[dest - 1] = this.ReadUint(addr + 1);
          break;

        /*
          --[ OUT a ]-------------------------------------------------

            Output byte
            Opcode: 0x02 (1 byte)

            Appends the value of `a` to the output stream.
        */
        case Instructions.OUT:
          this.Debug?.WriteLine($"OUT   {this.Reg8[(byte)Regs8.a]:X}('{(char)this.Reg8[(byte)Regs8.a]}')");
          this.Output?.WriteByte(this.Reg8[(byte)Regs8.a]);
          break;

        /*
          --[ SUB a <- b ]--------------------------------------------

            8-bit subtraction
            Opcode: 0xC3 (1 byte)

            Sets `a` to the result of subtracting `b` from `a`. If
            subtraction would result in a negative number, 256 is
            added to ensure that the result is non-negative.
        */
        case Instructions.SUB:
          this.Debug?.WriteLine($"SUB   a <- {this.Reg8[(byte)Regs8.a]:X} - {this.Reg8[(byte)Regs8.b]:X} = {(byte)(this.Reg8[(byte)Regs8.a] - this.Reg8[(byte)Regs8.b]):X}");
          this.Reg8[(byte)Regs8.a] -= this.Reg8[(byte)Regs8.b];
          break;

        /*
          --[ XOR a <- b ]--------------------------------------------

            8-bit bitwise exclusive OR
            Opcode: 0xC4 (1 byte)

            Sets `a` to the bitwise exclusive OR of `a` and `b`.
          */
        case Instructions.XOR:
          this.Debug?.WriteLine($"XOR   a <- {this.Reg8[(byte)Regs8.a]:X} ^ {this.Reg8[(byte)Regs8.b]:X} = {(byte)(this.Reg8[(byte)Regs8.a] ^ this.Reg8[(byte)Regs8.b]):X}");
          this.Reg8[(byte)Regs8.a] ^= this.Reg8[(byte)Regs8.b];
          break;
      }

      return false;
    }

    private uint ReadUint(uint addr)
    {
      return (uint)(this.Mem[addr] | this.Mem[addr + 1] << 8 | this.Mem[addr + 3] << 16 | this.Mem[addr + 3] << 24);
    }
  }
}
