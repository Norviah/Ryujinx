using ARMeilleure.IntermediateRepresentation;
using System;
using System.Diagnostics;
using System.IO;

namespace ARMeilleure.CodeGen.X86
{
    class Assembler
    {
        private const int BadOp = 0;

        private const int OpModRMBits = 24;

        private const byte LockPrefix = 0xf0;

        [Flags]
        private enum InstFlags
        {
            None     = 0,
            RegOnly  = 1 << 0,
            Reg8Src  = 1 << 1,
            Reg8Dest = 1 << 2,
            NoRexW   = 1 << 3,
            RexW     = 1 << 4,
            Vex      = 1 << 5,

            PrefixBit  = 16,
            PrefixMask = 3 << PrefixBit,
            Prefix66   = 1 << PrefixBit,
            PrefixF3   = 2 << PrefixBit,
            PrefixF2   = 3 << PrefixBit
        }

        private struct InstInfo
        {
            public int OpRMR     { get; }
            public int OpRMImm8  { get; }
            public int OpRMImm32 { get; }
            public int OpRImm64  { get; }
            public int OpRRM     { get; }

            public InstFlags Flags { get; }

            public InstInfo(
                int       opRMR,
                int       opRMImm8,
                int       opRMImm32,
                int       opRImm64,
                int       opRRM,
                InstFlags flags)
            {
                OpRMR     = opRMR;
                OpRMImm8  = opRMImm8;
                OpRMImm32 = opRMImm32;
                OpRImm64  = opRImm64;
                OpRRM     = opRRM;
                Flags     = flags;
            }
        }

        private static InstInfo[] _instTable;

        private Stream _stream;

        static Assembler()
        {
            _instTable = new InstInfo[(int)X86Instruction.Count];

            //  Name                                    RM/R        RM/I8       RM/I32      R/I64       R/RM        Flags
            Add(X86Instruction.Add,        new InstInfo(0x00000001, 0x00000083, 0x00000081, BadOp,      0x00000003, InstFlags.None));
            Add(X86Instruction.Addpd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f58, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Addps,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f58, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Addsd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f58, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF2));
            Add(X86Instruction.Addss,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f58, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.And,        new InstInfo(0x00000021, 0x04000083, 0x04000081, BadOp,      0x00000023, InstFlags.None));
            Add(X86Instruction.Andnpd,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f55, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Andnps,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f55, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Bsr,        new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fbd, InstFlags.None));
            Add(X86Instruction.Bswap,      new InstInfo(0x00000fc8, BadOp,      BadOp,      BadOp,      BadOp,      InstFlags.RegOnly));
            Add(X86Instruction.Call,       new InstInfo(0x020000ff, BadOp,      BadOp,      BadOp,      BadOp,      InstFlags.NoRexW));
            Add(X86Instruction.Cmovcc,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f40, InstFlags.None));
            Add(X86Instruction.Cmp,        new InstInfo(0x00000039, 0x07000083, 0x07000081, BadOp,      0x0000003b, InstFlags.None));
            Add(X86Instruction.Cmppd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fc2, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Cmpps,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fc2, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Cmpsd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fc2, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF2));
            Add(X86Instruction.Cmpss,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fc2, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Cmpxchg16b, new InstInfo(0x01000fc7, BadOp,      BadOp,      BadOp,      BadOp,      InstFlags.RexW));
            Add(X86Instruction.Comisd,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f2f, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Comiss,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f2f, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Cvtdq2pd,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fe6, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Cvtdq2ps,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5b, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Cvtpd2dq,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fe6, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF2));
            Add(X86Instruction.Cvtpd2ps,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5a, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Cvtps2dq,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5b, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Cvtps2pd,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5a, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Cvtsd2si,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f2c, InstFlags.Vex | InstFlags.PrefixF2));
            Add(X86Instruction.Cvtsd2ss,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5a, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF2));
            Add(X86Instruction.Cvtsi2sd,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f2a, InstFlags.Vex | InstFlags.PrefixF2));
            Add(X86Instruction.Cvtsi2ss,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f2a, InstFlags.Vex | InstFlags.PrefixF3));
            Add(X86Instruction.Cvtss2sd,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5a, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Div,        new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x060000f7, InstFlags.None));
            Add(X86Instruction.Divpd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5e, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Divps,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5e, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Divsd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5e, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF2));
            Add(X86Instruction.Divss,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5e, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Haddpd,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f7c, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Haddps,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f7c, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF2));
            Add(X86Instruction.Idiv,       new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x070000f7, InstFlags.None));
            Add(X86Instruction.Imul,       new InstInfo(BadOp,      0x0000006b, 0x00000069, BadOp,      0x00000faf, InstFlags.None));
            Add(X86Instruction.Imul128,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x050000f7, InstFlags.None));
            Add(X86Instruction.Insertps,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3a21, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Lea,        new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x0000008d, InstFlags.None));
            Add(X86Instruction.Maxpd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5f, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Maxps,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5f, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Maxsd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5f, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF2));
            Add(X86Instruction.Maxss,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5f, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Minpd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5d, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Minps,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5d, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Minsd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5d, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF2));
            Add(X86Instruction.Minss,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5d, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Mov,        new InstInfo(0x00000089, BadOp,      0x000000c7, 0x000000b8, 0x0000008b, InstFlags.None));
            Add(X86Instruction.Mov16,      new InstInfo(0x00000089, BadOp,      0x000000c7, BadOp,      0x0000008b, InstFlags.Prefix66));
            Add(X86Instruction.Mov8,       new InstInfo(0x00000088, 0x000000c6, BadOp,      BadOp,      0x0000008a, InstFlags.Reg8Src | InstFlags.Reg8Dest));
            Add(X86Instruction.Movd,       new InstInfo(0x00000f7e, BadOp,      BadOp,      BadOp,      0x00000f6e, InstFlags.Vex | InstFlags.Prefix66));
            Add(X86Instruction.Movdqu,     new InstInfo(0x00000f7f, BadOp,      BadOp,      BadOp,      0x00000f6f, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Movhlps,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f12, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Movlhps,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f16, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Movq,       new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f7e, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Movsd,      new InstInfo(0x00000f11, BadOp,      BadOp,      BadOp,      0x00000f10, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF2));
            Add(X86Instruction.Movss,      new InstInfo(0x00000f11, BadOp,      BadOp,      BadOp,      0x00000f10, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Movsx16,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fbf, InstFlags.None));
            Add(X86Instruction.Movsx32,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000063, InstFlags.None));
            Add(X86Instruction.Movsx8,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fbe, InstFlags.Reg8Src));
            Add(X86Instruction.Movzx16,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fb7, InstFlags.None));
            Add(X86Instruction.Movzx8,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fb6, InstFlags.Reg8Src));
            Add(X86Instruction.Mul128,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x040000f7, InstFlags.None));
            Add(X86Instruction.Mulpd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f59, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Mulps,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f59, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Mulsd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f59, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF2));
            Add(X86Instruction.Mulss,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f59, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Neg,        new InstInfo(0x030000f7, BadOp,      BadOp,      BadOp,      BadOp,      InstFlags.None));
            Add(X86Instruction.Not,        new InstInfo(0x020000f7, BadOp,      BadOp,      BadOp,      BadOp,      InstFlags.None));
            Add(X86Instruction.Or,         new InstInfo(0x00000009, 0x01000083, 0x01000081, BadOp,      0x0000000b, InstFlags.None));
            Add(X86Instruction.Paddb,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000ffc, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Paddd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000ffe, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Paddq,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fd4, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Paddw,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000ffd, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pand,       new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fdb, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pandn,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fdf, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pavgb,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fe0, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pavgw,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fe3, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pblendvb,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3a4c, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pcmpeqb,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f74, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pcmpeqd,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f76, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pcmpeqq,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3829, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pcmpeqw,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f75, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pcmpgtb,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f64, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pcmpgtd,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f66, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pcmpgtq,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3837, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pcmpgtw,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f65, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pextrb,     new InstInfo(0x000f3a14, BadOp,      BadOp,      BadOp,      BadOp,      InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pextrd,     new InstInfo(0x000f3a16, BadOp,      BadOp,      BadOp,      BadOp,      InstFlags.Vex | InstFlags.Prefix66));
            Add(X86Instruction.Pextrw,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fc5, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pinsrb,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3a20, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pinsrd,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3a22, InstFlags.Vex | InstFlags.Prefix66));
            Add(X86Instruction.Pinsrw,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fc4, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmaxsb,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f383c, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmaxsd,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f383d, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmaxsw,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fee, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmaxub,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fde, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmaxud,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f383f, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmaxuw,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f383e, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pminsb,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3838, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pminsd,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3839, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pminsw,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fea, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pminub,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fda, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pminud,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f383b, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pminuw,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f383a, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmovsxbw,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3820, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmovsxdq,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3825, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmovsxwd,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3823, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmovzxbw,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3830, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmovzxdq,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3835, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmovzxwd,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3833, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmulld,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3840, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pmullw,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fd5, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pop,        new InstInfo(0x0000008f, BadOp,      BadOp,      BadOp,      BadOp,      InstFlags.None));
            Add(X86Instruction.Popcnt,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fb8, InstFlags.PrefixF3));
            Add(X86Instruction.Por,        new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000feb, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pshufb,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3800, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pshufd,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f70, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pslld,      new InstInfo(BadOp,      0x06000f72, BadOp,      BadOp,      0x00000ff2, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Pslldq,     new InstInfo(BadOp,      0x07000f73, BadOp,      BadOp,      BadOp,      InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Psllq,      new InstInfo(BadOp,      0x06000f73, BadOp,      BadOp,      0x00000ff3, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Psllw,      new InstInfo(BadOp,      0x06000f71, BadOp,      BadOp,      0x00000ff1, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Psrad,      new InstInfo(BadOp,      0x04000f72, BadOp,      BadOp,      0x00000fe2, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Psraw,      new InstInfo(BadOp,      0x04000f71, BadOp,      BadOp,      0x00000fe1, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Psrld,      new InstInfo(BadOp,      0x02000f72, BadOp,      BadOp,      0x00000fd2, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Psrlq,      new InstInfo(BadOp,      0x02000f73, BadOp,      BadOp,      0x00000fd3, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Psrldq,     new InstInfo(BadOp,      0x03000f73, BadOp,      BadOp,      BadOp,      InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Psrlw,      new InstInfo(BadOp,      0x02000f71, BadOp,      BadOp,      0x00000fd1, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Psubb,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000ff8, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Psubd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000ffa, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Psubq,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000ffb, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Psubw,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000ff9, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Punpckhbw,  new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f68, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Punpckhdq,  new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f6a, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Punpckhqdq, new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f6d, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Punpckhwd,  new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f69, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Punpcklbw,  new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f60, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Punpckldq,  new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f62, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Punpcklqdq, new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f6c, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Punpcklwd,  new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f61, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Push,       new InstInfo(BadOp,      0x0000006a, 0x00000068, BadOp,      0x060000ff, InstFlags.None));
            Add(X86Instruction.Pxor,       new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fef, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Rcpps,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f53, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Rcpss,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f53, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Ror,        new InstInfo(0x010000d3, 0x010000c1, BadOp,      BadOp,      BadOp,      InstFlags.None));
            Add(X86Instruction.Roundpd,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3a09, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Roundps,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3a08, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Roundsd,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3a0b, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Roundss,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x000f3a0a, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Rsqrtps,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f52, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Rsqrtss,    new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f52, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Sar,        new InstInfo(0x070000d3, 0x070000c1, BadOp,      BadOp,      BadOp,      InstFlags.None));
            Add(X86Instruction.Setcc,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f90, InstFlags.Reg8Dest));
            Add(X86Instruction.Shl,        new InstInfo(0x040000d3, 0x040000c1, BadOp,      BadOp,      BadOp,      InstFlags.None));
            Add(X86Instruction.Shr,        new InstInfo(0x050000d3, 0x050000c1, BadOp,      BadOp,      BadOp,      InstFlags.None));
            Add(X86Instruction.Shufpd,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fc6, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Shufps,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000fc6, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Sqrtpd,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f51, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Sqrtps,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f51, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Sqrtsd,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f51, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF2));
            Add(X86Instruction.Sqrtss,     new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f51, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Sub,        new InstInfo(0x00000029, 0x05000083, 0x05000081, BadOp,      0x0000002b, InstFlags.None));
            Add(X86Instruction.Subpd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5c, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Subps,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5c, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Subsd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5c, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF2));
            Add(X86Instruction.Subss,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f5c, InstFlags.Vex | InstFlags.NoRexW | InstFlags.PrefixF3));
            Add(X86Instruction.Test,       new InstInfo(0x00000085, BadOp,      0x000000f7, BadOp,      BadOp,      InstFlags.None));
            Add(X86Instruction.Unpckhpd,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f15, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Unpckhps,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f15, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Unpcklpd,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f14, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Unpcklps,   new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f14, InstFlags.Vex | InstFlags.NoRexW));
            Add(X86Instruction.Xor,        new InstInfo(0x00000031, 0x06000083, 0x06000081, BadOp,      0x00000033, InstFlags.None));
            Add(X86Instruction.Xorpd,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f57, InstFlags.Vex | InstFlags.NoRexW | InstFlags.Prefix66));
            Add(X86Instruction.Xorps,      new InstInfo(BadOp,      BadOp,      BadOp,      BadOp,      0x00000f57, InstFlags.Vex | InstFlags.NoRexW));
        }

        private static void Add(X86Instruction inst, InstInfo info)
        {
            _instTable[(int)inst] = info;
        }

        public Assembler(Stream stream)
        {
            _stream = stream;
        }

        public void Add(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Add);
        }

        public void Addsd(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Addsd, source1);
        }

        public void Addss(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Addss, source1);
        }

        public void And(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.And);
        }

        public void Bsr(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Bsr);
        }

        public void Bswap(Operand dest)
        {
            WriteInstruction(dest, null, X86Instruction.Bswap);
        }

        public void Call(Operand dest)
        {
            WriteInstruction(dest, null, X86Instruction.Call);
        }

        public void Cdq()
        {
            WriteByte(0x99);
        }

        public void Cmovcc(Operand dest, Operand source, X86Condition condition)
        {
            InstInfo info = _instTable[(int)X86Instruction.Cmovcc];

            WriteRRMOpCode(dest, source, info.Flags, info.OpRRM | (int)condition);
        }

        public void Cmp(Operand source1, Operand source2)
        {
            WriteInstruction(source1, source2, X86Instruction.Cmp);
        }

        public void Cqo()
        {
            WriteByte(0x48);
            WriteByte(0x99);
        }

        public void Cmpxchg16b(X86MemoryOperand memOp)
        {
            WriteByte(LockPrefix);

            WriteInstruction(memOp, null, X86Instruction.Cmpxchg16b);
        }

        public void Comisd(Operand source1, Operand source2)
        {
            WriteInstruction(source1, source2, X86Instruction.Comisd);
        }

        public void Comiss(Operand source1, Operand source2)
        {
            WriteInstruction(source1, source2, X86Instruction.Comiss);
        }

        public void Cvtsd2ss(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Cvtsd2ss, source1);
        }

        public void Cvtsi2sd(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Cvtsi2sd, source1);
        }

        public void Cvtsi2ss(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Cvtsi2ss, source1);
        }

        public void Cvtss2sd(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Cvtss2sd, source1);
        }

        public void Div(Operand source)
        {
            WriteInstruction(null, source, X86Instruction.Div);
        }

        public void Divsd(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Divsd, source1);
        }

        public void Divss(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Divss, source1);
        }

        public void Idiv(Operand source)
        {
            WriteInstruction(null, source, X86Instruction.Idiv);
        }

        public void Imul(Operand source)
        {
            WriteInstruction(null, source, X86Instruction.Imul128);
        }

        public void Imul(Operand dest, Operand source)
        {
            if (source.Kind != OperandKind.Register)
            {
                throw new ArgumentException($"Invalid source operand kind \"{source.Kind}\".");
            }

            WriteInstruction(dest, source, X86Instruction.Imul);
        }

        public void Imul(Operand dest, Operand src1, Operand src2)
        {
            InstInfo info = _instTable[(int)X86Instruction.Imul];

            if (src2.Kind != OperandKind.Constant)
            {
                throw new ArgumentException($"Invalid source 2 operand kind \"{src2.Kind}\".");
            }

            if (IsImm8(src2) && info.OpRMImm8 != BadOp)
            {
                WriteRRMOpCode(dest, src1, info.Flags, info.OpRMImm8);

                WriteByte(src2.AsByte());
            }
            else if (IsImm32(src2) && info.OpRMImm32 != BadOp)
            {
                WriteRRMOpCode(dest, src1, info.Flags, info.OpRMImm32);

                WriteInt32(src2.AsInt32());
            }
            else
            {
                throw new ArgumentException($"Failed to encode constant 0x{src2.Value:X}.");
            }
        }

        public void Insertps(Operand dest, Operand source, Operand source1, byte imm)
        {
            WriteInstruction(dest, source, X86Instruction.Insertps, source1);

            WriteByte(imm);
        }

        public void Jcc(X86Condition condition, long offset)
        {
            if (ConstFitsOnS8(offset))
            {
                WriteByte((byte)(0x70 | (int)condition));

                WriteByte((byte)offset);
            }
            else if (ConstFitsOnS32(offset))
            {
                WriteByte(0x0f);
                WriteByte((byte)(0x80 | (int)condition));

                WriteInt32((int)offset);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
        }

        public void Jmp(long offset)
        {
            if (ConstFitsOnS8(offset))
            {
                WriteByte(0xeb);

                WriteByte((byte)offset);
            }
            else if (ConstFitsOnS32(offset))
            {
                WriteByte(0xe9);

                WriteInt32((int)offset);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
        }

        public void Lea(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Lea);
        }

        public void Mov(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Mov);
        }

        public void Mov16(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Mov16);
        }

        public void Mov8(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Mov8);
        }

        public void Movd(Operand dest, Operand source)
        {
            InstInfo info = _instTable[(int)X86Instruction.Movd];

            if (dest.GetRegister().Type == RegisterType.Vector || source.Kind == OperandKind.Memory)
            {
                WriteRRMOpCode(dest, source, info.Flags, info.OpRRM);
            }
            else
            {
                WriteOpCode(dest, source, info.Flags, info.OpRMR);
            }
        }

        public void Movdqu(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Movdqu);
        }

        public void Movhlps(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Movhlps, source1);
        }

        public void Movlhps(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Movlhps, source1);
        }

        public void Movq(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Movq);
        }

        public void Movsd(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Movsd, source1);
        }

        public void Movss(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Movss, source1);
        }

        public void Movsx16(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Movsx16);
        }

        public void Movsx32(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Movsx32);
        }

        public void Movsx8(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Movsx8);
        }

        public void Movzx16(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Movzx16);
        }

        public void Movzx8(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Movzx8);
        }

        public void Mul(Operand source)
        {
            WriteInstruction(null, source, X86Instruction.Mul128);
        }

        public void Mulsd(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Mulsd, source1);
        }

        public void Mulss(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Mulss, source1);
        }

        public void Neg(Operand dest)
        {
            WriteInstruction(dest, null, X86Instruction.Neg);
        }

        public void Not(Operand dest)
        {
            WriteInstruction(dest, null, X86Instruction.Not);
        }

        public void Or(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Or);
        }

        public void Pcmpeqw(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Pcmpeqw, source1);
        }

        public void Pextrb(Operand dest, Operand source, byte imm)
        {
            WriteInstruction(dest, source, X86Instruction.Pextrb);

            WriteByte(imm);
        }

        public void Pextrd(Operand dest, Operand source, byte imm)
        {
            WriteInstruction(dest, source, X86Instruction.Pextrd);

            WriteByte(imm);
        }

        public void Pextrw(Operand dest, Operand source, byte imm)
        {
            WriteInstruction(dest, source, X86Instruction.Pextrw);

            WriteByte(imm);
        }

        public void Pinsrb(Operand dest, Operand source, Operand source1, byte imm)
        {
            WriteInstruction(dest, source, X86Instruction.Pinsrb, source1);

            WriteByte(imm);
        }

        public void Pinsrd(Operand dest, Operand source, Operand source1, byte imm)
        {
            WriteInstruction(dest, source, X86Instruction.Pinsrd, source1);

            WriteByte(imm);
        }

        public void Pinsrw(Operand dest, Operand source, Operand source1, byte imm)
        {
            WriteInstruction(dest, source, X86Instruction.Pinsrw, source1);

            WriteByte(imm);
        }

        public void Pop(Operand dest)
        {
            if (dest.Kind == OperandKind.Register)
            {
                WriteCompactInst(dest, 0x58);
            }
            else
            {
                WriteInstruction(dest, null, X86Instruction.Pop);
            }
        }

        public void Popcnt(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Popcnt);
        }

        public void Pshufd(Operand dest, Operand source, byte imm)
        {
            WriteInstruction(dest, source, X86Instruction.Pshufd);

            WriteByte(imm);
        }

        public void Push(Operand source)
        {
            if (source.Kind == OperandKind.Register)
            {
                WriteCompactInst(source, 0x50);
            }
            else
            {
                WriteInstruction(null, source, X86Instruction.Push);
            }
        }

        public void Return()
        {
            WriteByte(0xc3);
        }

        public void Ror(Operand dest, Operand source)
        {
            WriteShiftInst(dest, source, X86Instruction.Ror);
        }

        public void Sar(Operand dest, Operand source)
        {
            WriteShiftInst(dest, source, X86Instruction.Sar);
        }

        public void Shl(Operand dest, Operand source)
        {
            WriteShiftInst(dest, source, X86Instruction.Shl);
        }

        public void Shr(Operand dest, Operand source)
        {
            WriteShiftInst(dest, source, X86Instruction.Shr);
        }

        public void Setcc(Operand dest, X86Condition condition)
        {
            InstInfo info = _instTable[(int)X86Instruction.Setcc];

            WriteOpCode(dest, null, info.Flags, info.OpRRM | (int)condition);
        }

        public void Sub(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Sub);
        }

        public void Subsd(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Subsd, source1);
        }

        public void Subss(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Subss, source1);
        }

        public void Test(Operand src1, Operand src2)
        {
            WriteInstruction(src1, src2, X86Instruction.Test);
        }

        public void Xor(Operand dest, Operand source)
        {
            WriteInstruction(dest, source, X86Instruction.Xor);
        }

        public void Xorps(Operand dest, Operand source, Operand source1)
        {
            WriteInstruction(dest, source, X86Instruction.Xorps, source1);
        }

        public void WriteInstruction(X86Instruction inst, Operand dest, Operand source)
        {
            WriteInstruction(dest, source, inst);
        }

        public void WriteInstruction(X86Instruction inst, Operand dest, Operand src1, Operand src2)
        {
            if (src2.Kind == OperandKind.Constant)
            {
                WriteInstruction(src1, src2, inst, dest);
            }
            else
            {
                WriteInstruction(dest, src2, inst, src1);
            }
        }

        public void WriteInstruction(X86Instruction inst, Operand dest, Operand source, byte imm)
        {
            WriteInstruction(dest, source, inst);

            WriteByte(imm);
        }

        public void WriteInstruction(
            X86Instruction inst,
            Operand dest,
            Operand src1,
            Operand src2,
            Operand src3)
        {
            //TODO: Non-VEX version.
            WriteInstruction(dest, src2, inst, src1);

            WriteByte((byte)(src3.AsByte() << 4));
        }

        public void WriteInstruction(
            X86Instruction inst,
            Operand dest,
            Operand src1,
            Operand src2,
            byte imm)
        {
            WriteInstruction(dest, src2, inst, src1);

            WriteByte(imm);
        }

        private void WriteShiftInst(Operand dest, Operand source, X86Instruction inst)
        {
            if (source.Kind == OperandKind.Register)
            {
                X86Register shiftReg = (X86Register)source.GetRegister().Index;

                if (shiftReg != X86Register.Rcx)
                {
                    throw new ArgumentException($"Invalid shift register \"{shiftReg}\".");
                }

                source = null;
            }

            WriteInstruction(dest, source, inst);
        }

        private void WriteInstruction(Operand dest, Operand source, X86Instruction inst, Operand source1 = null)
        {
            InstInfo info = _instTable[(int)inst];

            if (source != null)
            {
                if (source.Kind == OperandKind.Constant)
                {
                    if (inst == X86Instruction.Mov8)
                    {
                        WriteOpCode(dest, null, info.Flags, info.OpRMImm8, source1);

                        WriteByte(source.AsByte());
                    }
                    else if (inst == X86Instruction.Mov16)
                    {
                        WriteOpCode(dest, null, info.Flags, info.OpRMImm32, source1);

                        WriteInt16(source.AsInt16());
                    }
                    else if (IsImm8(source) && info.OpRMImm8 != BadOp)
                    {
                        WriteOpCode(dest, null, info.Flags, info.OpRMImm8, source1);

                        WriteByte(source.AsByte());
                    }
                    else if (IsImm32(source) && info.OpRMImm32 != BadOp)
                    {
                        WriteOpCode(dest, null, info.Flags, info.OpRMImm32, source1);

                        WriteInt32(source.AsInt32());
                    }
                    else if (dest != null && IsR64(dest) && info.OpRImm64 != BadOp)
                    {
                        int rexPrefix = GetRexPrefix(dest, source, rexW: true, rrm: false);

                        if (rexPrefix != 0)
                        {
                            WriteByte((byte)rexPrefix);
                        }

                        WriteByte((byte)(info.OpRImm64 + (dest.GetRegister().Index & 0b111)));

                        WriteUInt64(source.Value);
                    }
                    else
                    {
                        throw new ArgumentException($"Failed to encode constant 0x{source.Value:X}.");
                    }
                }
                else if (source.Kind == OperandKind.Register && info.OpRMR != BadOp)
                {
                    WriteOpCode(dest, source, info.Flags, info.OpRMR, source1);
                }
                else if (info.OpRRM != BadOp)
                {
                    WriteRRMOpCode(dest, source, info.Flags, info.OpRRM, source1);
                }
                else
                {
                    throw new ArgumentException($"Invalid source operand kind \"{source.Kind}\".");
                }
            }
            else if (info.OpRRM != BadOp)
            {
                WriteRRMOpCode(dest, source, info.Flags, info.OpRRM, source1);
            }
            else if (info.OpRMR != BadOp)
            {
                WriteOpCode(dest, source, info.Flags, info.OpRMR, source1);
            }
            else
            {
                throw new ArgumentNullException(nameof(source));
            }
        }

        private void WriteRRMOpCode(
            Operand   dest,
            Operand   source,
            InstFlags flags,
            int       opCode,
            Operand   source1 = null)
        {
            WriteOpCode(dest, source, flags, opCode, source1, rrm: true);
        }

        private void WriteOpCode(
            Operand   dest,
            Operand   source,
            InstFlags flags,
            int       opCode,
            Operand   source1 = null,
            bool      rrm     = false)
        {
            bool rexW = (flags & InstFlags.NoRexW) == 0;

            int rexPrefix = GetRexPrefix(dest, source, rexW, rrm);

            int modRM = (opCode >> OpModRMBits) << 3;

            X86MemoryOperand memOp = null;

            if (dest != null)
            {
                if (dest.Kind == OperandKind.Register)
                {
                    int regIndex = dest.GetRegister().Index;

                    modRM |= (regIndex & 0b111) << (rrm ? 3 : 0);

                    if ((flags & InstFlags.Reg8Dest) != 0 && regIndex >= 4)
                    {
                        rexPrefix |= 0x40;
                    }
                }
                else if (dest.Kind == OperandKind.Memory)
                {
                    memOp = (X86MemoryOperand)dest;
                }
                else
                {
                    throw new ArgumentException("Invalid destination operand kind \"" + dest.Kind + "\".");
                }
            }

            if (source != null)
            {
                if (source.Kind == OperandKind.Register)
                {
                    int regIndex = source.GetRegister().Index;

                    modRM |= (regIndex & 0b111) << (rrm ? 0 : 3);

                    if ((flags & InstFlags.Reg8Src) != 0 && regIndex >= 4)
                    {
                        rexPrefix |= 0x40;
                    }
                }
                else if (source.Kind == OperandKind.Memory && memOp == null)
                {
                    memOp = (X86MemoryOperand)source;
                }
                else
                {
                    throw new ArgumentException("Invalid source operand kind \"" + source.Kind + "\".");
                }
            }

            bool needsSibByte = false;

            bool needsDisplacement = false;

            int sib = 0;

            if (memOp != null)
            {
                //Either source or destination is a memory operand.
                Register baseReg = memOp.BaseAddress.GetRegister();

                X86Register baseRegLow = (X86Register)(baseReg.Index & 0b111);

                needsSibByte = memOp.Index != null || baseRegLow == X86Register.Rsp;

                needsDisplacement = memOp.Displacement != 0 || baseRegLow == X86Register.Rbp;

                if (needsDisplacement)
                {
                    if (ConstFitsOnS8(memOp.Displacement))
                    {
                        modRM |= 0x40;
                    }
                    else /* if (ConstFitsOnS32(memOp.Displacement)) */
                    {
                        modRM |= 0x80;
                    }
                }

                if (baseReg.Index >= 8)
                {
                    rexPrefix |= 0x40 | (baseReg.Index >> 3);
                }

                if (needsSibByte)
                {
                    sib = (int)baseRegLow;

                    if (memOp.Index != null)
                    {
                        int indexReg = memOp.Index.GetRegister().Index;

                        if (indexReg == (int)X86Register.Rsp)
                        {
                            throw new ArgumentException("Using RSP as index register on the memory operand is not allowed.");
                        }

                        if (indexReg >= 8)
                        {
                            rexPrefix |= 0x40 | (indexReg >> 3) << 1;
                        }

                        sib |= (indexReg & 0b111) << 3;
                    }
                    else
                    {
                        sib |= 0b100 << 3;
                    }

                    sib |= (int)memOp.Scale << 6;

                    modRM |= 0b100;
                }
                else
                {
                    modRM |= (int)baseRegLow;
                }
            }
            else
            {
                //Source and destination are registers.
                modRM |= 0xc0;
            }

            Debug.Assert(opCode != BadOp, "Invalid opcode value.");

            if ((flags & InstFlags.Vex) != 0 && HardwareCapabilities.SupportsVexEncoding)
            {
                int vexByte2 = (int)(flags & InstFlags.PrefixMask) >> (int)InstFlags.PrefixBit;

                if (source1 != null)
                {
                    vexByte2 |= (source1.GetRegister().Index ^ 0xf) << 3;
                }
                else
                {
                    vexByte2 |= 0b1111 << 3;
                }

                ushort opCodeHigh = (ushort)(opCode >> 8);

                if ((rexPrefix & 0b1011) == 0 && opCodeHigh == 0xf)
                {
                    //Two-byte form.
                    WriteByte(0xc5);

                    vexByte2 |= (~rexPrefix & 4) << 5;

                    WriteByte((byte)vexByte2);
                }
                else
                {
                    //Three-byte form.
                    WriteByte(0xc4);

                    int vexByte1 = (~rexPrefix & 7) << 5;

                    switch (opCodeHigh)
                    {
                        case 0xf:   vexByte1 |= 1; break;
                        case 0xf38: vexByte1 |= 2; break;
                        case 0xf3a: vexByte1 |= 3; break;

                        default: Debug.Assert(false, $"Failed to VEX encode opcode 0x{opCode:X}."); break;
                    }

                    vexByte2 |= (rexPrefix & 8) << 4;

                    WriteByte((byte)vexByte1);
                    WriteByte((byte)vexByte2);
                }

                opCode &= 0xff;
            }
            else
            {
                switch (flags & InstFlags.PrefixMask)
                {
                    case InstFlags.Prefix66: WriteByte(0x66); break;
                    case InstFlags.PrefixF2: WriteByte(0xf2); break;
                    case InstFlags.PrefixF3: WriteByte(0xf3); break;
                }

                if (rexPrefix != 0)
                {
                    WriteByte((byte)rexPrefix);
                }
            }

            if ((flags & InstFlags.RegOnly) != 0)
            {
                opCode += dest.GetRegister().Index & 7;
            }

            if ((opCode & 0xff0000) != 0)
            {
                WriteByte((byte)(opCode >> 16));
            }

            if ((opCode & 0xff00) != 0)
            {
                WriteByte((byte)(opCode >> 8));
            }

            WriteByte((byte)opCode);

            if ((flags & InstFlags.RegOnly) == 0)
            {
                WriteByte((byte)modRM);

                if (needsSibByte)
                {
                    WriteByte((byte)sib);
                }

                if (needsDisplacement)
                {
                    if (ConstFitsOnS8(memOp.Displacement))
                    {
                        WriteByte((byte)memOp.Displacement);
                    }
                    else /* if (ConstFitsOnS32(memOp.Displacement)) */
                    {
                        WriteInt32(memOp.Displacement);
                    }
                }
            }
        }

        private void WriteCompactInst(Operand operand, int opCode)
        {
            int regIndex = operand.GetRegister().Index;

            if (regIndex >= 8)
            {
                WriteByte(0x41);
            }

            WriteByte((byte)(opCode + (regIndex & 0b111)));
        }

        private static int GetRexPrefix(Operand dest, Operand source, bool rexW, bool rrm)
        {
            int rexPrefix = 0;

            void SetRegisterHighBit(Register reg, int bit)
            {
                if (reg.Index >= 8)
                {
                    rexPrefix |= 0x40 | (reg.Index >> 3) << bit;
                }
            }

            if (dest != null)
            {
                if (Is64Bits(dest.Type) && rexW)
                {
                    rexPrefix = 0x48;
                }

                if (dest.Kind == OperandKind.Register)
                {
                    SetRegisterHighBit(dest.GetRegister(), (rrm ? 2 : 0));
                }
            }

            if (source != null)
            {
                if (Is64Bits(source.Type) && rexW)
                {
                    rexPrefix |= 0x48;
                }

                if (source.Kind == OperandKind.Register)
                {
                    SetRegisterHighBit(source.GetRegister(), (rrm ? 0 : 2));
                }
            }

            return rexPrefix;
        }

        private static bool IsR64(Operand operand)
        {
            return operand.Kind == OperandKind.Register && operand.Type == OperandType.I64;
        }

        private static bool Is64Bits(OperandType type)
        {
            return type == OperandType.I64 || type == OperandType.FP64;
        }

        private static bool IsImm8(Operand operand)
        {
            long value = operand.Type == OperandType.I32 ? operand.AsInt32()
                                                         : operand.AsInt64();

            return ConstFitsOnS8(value);
        }

        private static bool IsImm32(Operand operand)
        {
            long value = operand.Type == OperandType.I32 ? operand.AsInt32()
                                                         : operand.AsInt64();

            return ConstFitsOnS32(value);
        }

        public static int GetJccLength(long offset)
        {
            if (ConstFitsOnS8(offset < 0 ? offset - 2 : offset))
            {
                return 2;
            }
            else if (ConstFitsOnS32(offset < 0 ? offset - 6 : offset))
            {
                return 6;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
        }

        public static int GetJmpLength(long offset)
        {
            if (ConstFitsOnS8(offset < 0 ? offset - 2 : offset))
            {
                return 2;
            }
            else if (ConstFitsOnS32(offset < 0 ? offset - 5 : offset))
            {
                return 5;
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
        }

        private static bool ConstFitsOnU32(long value)
        {
            return value >> 32 == 0;
        }

        private static bool ConstFitsOnS8(long value)
        {
            return value == (sbyte)value;
        }

        private static bool ConstFitsOnS32(long value)
        {
            return value == (int)value;
        }

        private void WriteInt16(short value)
        {
            WriteUInt16((ushort)value);
        }

        private void WriteInt32(int value)
        {
            WriteUInt32((uint)value);
        }

        private void WriteInt64(long value)
        {
            WriteUInt64((ulong)value);
        }

        private void WriteByte(byte value)
        {
            _stream.WriteByte(value);
        }

        private void WriteUInt16(ushort value)
        {
            _stream.WriteByte((byte)(value >> 0));
            _stream.WriteByte((byte)(value >> 8));
        }

        private void WriteUInt32(uint value)
        {
            _stream.WriteByte((byte)(value >> 0));
            _stream.WriteByte((byte)(value >> 8));
            _stream.WriteByte((byte)(value >> 16));
            _stream.WriteByte((byte)(value >> 24));
        }

        private void WriteUInt64(ulong value)
        {
            _stream.WriteByte((byte)(value >> 0));
            _stream.WriteByte((byte)(value >> 8));
            _stream.WriteByte((byte)(value >> 16));
            _stream.WriteByte((byte)(value >> 24));
            _stream.WriteByte((byte)(value >> 32));
            _stream.WriteByte((byte)(value >> 40));
            _stream.WriteByte((byte)(value >> 48));
            _stream.WriteByte((byte)(value >> 56));
        }
    }
}