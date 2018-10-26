using ChocolArm64.Decoder;
using ChocolArm64.State;
using ChocolArm64.Translation;
using System;
using System.Reflection.Emit;

using static ChocolArm64.Instruction.AInstEmitMemoryHelper;
using static ChocolArm64.Instruction.AInstEmitSimdHelper;

namespace ChocolArm64.Instruction
{
    internal static partial class AInstEmit
    {
        public static void Ld__Vms(AILEmitterCtx context)
        {
            EmitSimdMemMs(context, true);
        }

        public static void Ld__Vss(AILEmitterCtx context)
        {
            EmitSimdMemSs(context, true);
        }

        public static void St__Vms(AILEmitterCtx context)
        {
            EmitSimdMemMs(context, false);
        }

        public static void St__Vss(AILEmitterCtx context)
        {
            EmitSimdMemSs(context, false);
        }

        private static void EmitSimdMemMs(AILEmitterCtx context, bool isLoad)
        {
            AOpCodeSimdMemMs op = (AOpCodeSimdMemMs)context.CurrOp;

            int offset = 0;

            for (int rep   = 0; rep   < op.Reps;   rep++)
            for (int elem  = 0; elem  < op.Elems;  elem++)
            for (int sElem = 0; sElem < op.SElems; sElem++)
            {
                int rtt = (op.Rt + rep + sElem) & 0x1f;

                if (isLoad)
                {
                    context.EmitLdarg(ATranslatedSub.MemoryArgIdx);
                    context.EmitLdint(op.Rn);
                    context.EmitLdc_I8(offset);

                    context.Emit(OpCodes.Add);

                    EmitReadZxCall(context, op.Size);

                    EmitVectorInsert(context, rtt, elem, op.Size);

                    if (op.RegisterSize == ARegisterSize.Simd64 && elem == op.Elems - 1) EmitVectorZeroUpper(context, rtt);
                }
                else
                {
                    context.EmitLdarg(ATranslatedSub.MemoryArgIdx);
                    context.EmitLdint(op.Rn);
                    context.EmitLdc_I8(offset);

                    context.Emit(OpCodes.Add);

                    EmitVectorExtractZx(context, rtt, elem, op.Size);

                    EmitWriteCall(context, op.Size);
                }

                offset += 1 << op.Size;
            }

            if (op.WBack) EmitSimdMemWBack(context, offset);
        }

        private static void EmitSimdMemSs(AILEmitterCtx context, bool isLoad)
        {
            AOpCodeSimdMemSs op = (AOpCodeSimdMemSs)context.CurrOp;

            int offset = 0;

            void EmitMemAddress()
            {
                context.EmitLdarg(ATranslatedSub.MemoryArgIdx);
                context.EmitLdint(op.Rn);
                context.EmitLdc_I8(offset);

                context.Emit(OpCodes.Add);
            }

            if (op.Replicate)
            {
                //Only loads uses the replicate mode.
                if (!isLoad) throw new InvalidOperationException();

                int bytes = op.GetBitsCount() >> 3;
                int elems = bytes >> op.Size;

                for (int sElem = 0; sElem < op.SElems; sElem++)
                {
                    int rt = (op.Rt + sElem) & 0x1f;

                    for (int index = 0; index < elems; index++)
                    {
                        EmitMemAddress();

                        EmitReadZxCall(context, op.Size);

                        EmitVectorInsert(context, rt, index, op.Size);
                    }

                    if (op.RegisterSize == ARegisterSize.Simd64) EmitVectorZeroUpper(context, rt);

                    offset += 1 << op.Size;
                }
            }
            else
            {
                for (int sElem = 0; sElem < op.SElems; sElem++)
                {
                    int rt = (op.Rt + sElem) & 0x1f;

                    if (isLoad)
                    {
                        EmitMemAddress();

                        EmitReadZxCall(context, op.Size);

                        EmitVectorInsert(context, rt, op.Index, op.Size);
                    }
                    else
                    {
                        EmitMemAddress();

                        EmitVectorExtractZx(context, rt, op.Index, op.Size);

                        EmitWriteCall(context, op.Size);
                    }

                    offset += 1 << op.Size;
                }
            }

            if (op.WBack) EmitSimdMemWBack(context, offset);
        }

        private static void EmitSimdMemWBack(AILEmitterCtx context, int offset)
        {
            AOpCodeMemReg op = (AOpCodeMemReg)context.CurrOp;

            context.EmitLdint(op.Rn);

            if (op.Rm != AThreadState.ZrIndex)
                context.EmitLdint(op.Rm);
            else
                context.EmitLdc_I8(offset);

            context.Emit(OpCodes.Add);

            context.EmitStint(op.Rn);
        }
    }
}