#define SimdRegElem

using ChocolArm64.State;

using NUnit.Framework;

using System.Runtime.Intrinsics;

namespace Ryujinx.Tests.Cpu
{
    [Category("SimdRegElem")] // Tested: second half of 2018.
    public sealed class CpuTestSimdRegElem : CpuTest
    {
#if SimdRegElem

#region "ValueSource (Types)"
        private static ulong[] _2S_()
        {
            return new ulong[] { 0x0000000000000000ul, 0x7FFFFFFF7FFFFFFFul,
                                 0x8000000080000000ul, 0xFFFFFFFFFFFFFFFFul };
        }

        private static ulong[] _4H_()
        {
            return new ulong[] { 0x0000000000000000ul, 0x7FFF7FFF7FFF7FFFul,
                                 0x8000800080008000ul, 0xFFFFFFFFFFFFFFFFul };
        }
#endregion

#region "ValueSource (Opcodes)"
        private static uint[] _Mla_Mls_Mul_Ve_4H_8H_()
        {
            return new uint[]
            {
                0x2F400000u, // MLA V0.4H, V0.4H, V0.H[0]
                0x2F404000u, // MLS V0.4H, V0.4H, V0.H[0]
                0x0F408000u  // MUL V0.4H, V0.4H, V0.H[0]
            };
        }

        private static uint[] _Mla_Mls_Mul_Ve_2S_4S_()
        {
            return new uint[]
            {
                0x2F800000u, // MLA V0.2S, V0.2S, V0.S[0]
                0x2F804000u, // MLS V0.2S, V0.2S, V0.S[0]
                0x0F808000u  // MUL V0.2S, V0.2S, V0.S[0]
            };
        }
#endregion

        private const int RndCnt = 2;

        [Test][Pairwise]
        public void Mla_Mls_Mul_Ve_4H_8H([ValueSource("_Mla_Mls_Mul_Ve_4H_8H_")] uint opcodes,
                                         [Values(0u)]     uint rd,
                                         [Values(1u, 0u)] uint rn,
                                         [Values(2u, 0u)] uint rm,
                                         [ValueSource("_4H_")] [Random(RndCnt)] ulong z,
                                         [ValueSource("_4H_")] [Random(RndCnt)] ulong a,
                                         [ValueSource("_4H_")] [Random(RndCnt)] ulong b,
                                         [Values(0u, 1u, 2u, 3u, 4u, 5u, 6u, 7u)] uint index,
                                         [Values(0b0u, 0b1u)] uint q) // <4H, 8H>
        {
            uint h = (index >> 2) & 1;
            uint l = (index >> 1) & 1;
            uint m = index & 1;

            opcodes |= ((rm & 15) << 16) | ((rn & 31) << 5) | ((rd & 31) << 0);
            opcodes |= (l << 21) | (m << 20) | (h << 11);
            opcodes |= (q & 1) << 30;

            Vector128<float> v0 = MakeVectorE0E1(z, z);
            Vector128<float> v1 = MakeVectorE0E1(a, a * q);
            Vector128<float> v2 = MakeVectorE0E1(b, b * h);

            AThreadState threadState = SingleOpcode(opcodes, v0: v0, v1: v1, v2: v2);

            CompareAgainstUnicorn();
        }

        [Test][Pairwise]
        public void Mla_Mls_Mul_Ve_2S_4S([ValueSource("_Mla_Mls_Mul_Ve_2S_4S_")] uint opcodes,
                                         [Values(0u)]     uint rd,
                                         [Values(1u, 0u)] uint rn,
                                         [Values(2u, 0u)] uint rm,
                                         [ValueSource("_2S_")] [Random(RndCnt)] ulong z,
                                         [ValueSource("_2S_")] [Random(RndCnt)] ulong a,
                                         [ValueSource("_2S_")] [Random(RndCnt)] ulong b,
                                         [Values(0u, 1u, 2u, 3u)] uint index,
                                         [Values(0b0u, 0b1u)] uint q) // <2S, 4S>
        {
            uint h = (index >> 1) & 1;
            uint l = index & 1;

            opcodes |= ((rm & 15) << 16) | ((rn & 31) << 5) | ((rd & 31) << 0);
            opcodes |= (l << 21) | (h << 11);
            opcodes |= (q & 1) << 30;

            Vector128<float> v0 = MakeVectorE0E1(z, z);
            Vector128<float> v1 = MakeVectorE0E1(a, a * q);
            Vector128<float> v2 = MakeVectorE0E1(b, b * h);

            AThreadState threadState = SingleOpcode(opcodes, v0: v0, v1: v1, v2: v2);

            CompareAgainstUnicorn();
        }
#endif
    }
}
