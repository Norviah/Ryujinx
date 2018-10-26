using ChocolArm64.Decoder;
using ChocolArm64.State;
using ChocolArm64.Translation;
using System.Reflection.Emit;

namespace ChocolArm64.Instruction
{
    internal static class AInstEmitAluHelper
    {
        public static void EmitAdcsCCheck(AILEmitterCtx context)
        {
            //C = (Rd == Rn && CIn) || Rd < Rn
            context.EmitSttmp();
            context.EmitLdtmp();
            context.EmitLdtmp();

            EmitDataLoadRn(context);

            context.Emit(OpCodes.Ceq);

            context.EmitLdflg((int)APState.CBit);

            context.Emit(OpCodes.And);

            context.EmitLdtmp();

            EmitDataLoadRn(context);

            context.Emit(OpCodes.Clt_Un);
            context.Emit(OpCodes.Or);

            context.EmitStflg((int)APState.CBit);
        }

        public static void EmitAddsCCheck(AILEmitterCtx context)
        {
            //C = Rd < Rn
            context.Emit(OpCodes.Dup);

            EmitDataLoadRn(context);

            context.Emit(OpCodes.Clt_Un);

            context.EmitStflg((int)APState.CBit);
        }

        public static void EmitAddsVCheck(AILEmitterCtx context)
        {
            //V = (Rd ^ Rn) & ~(Rn ^ Rm) < 0
            context.Emit(OpCodes.Dup);

            EmitDataLoadRn(context);

            context.Emit(OpCodes.Xor);

            EmitDataLoadOpers(context);

            context.Emit(OpCodes.Xor);
            context.Emit(OpCodes.Not);
            context.Emit(OpCodes.And);

            context.EmitLdc_I(0);

            context.Emit(OpCodes.Clt);

            context.EmitStflg((int)APState.VBit);
        }

        public static void EmitSbcsCCheck(AILEmitterCtx context)
        {
            //C = (Rn == Rm && CIn) || Rn > Rm
            EmitDataLoadOpers(context);

            context.Emit(OpCodes.Ceq);

            context.EmitLdflg((int)APState.CBit);

            context.Emit(OpCodes.And);

            EmitDataLoadOpers(context);

            context.Emit(OpCodes.Cgt_Un);
            context.Emit(OpCodes.Or);

            context.EmitStflg((int)APState.CBit);
        }

        public static void EmitSubsCCheck(AILEmitterCtx context)
        {
            //C = Rn == Rm || Rn > Rm = !(Rn < Rm)
            EmitDataLoadOpers(context);

            context.Emit(OpCodes.Clt_Un);

            context.EmitLdc_I4(1);

            context.Emit(OpCodes.Xor);

            context.EmitStflg((int)APState.CBit);
        }

        public static void EmitSubsVCheck(AILEmitterCtx context)
        {
            //V = (Rd ^ Rn) & (Rn ^ Rm) < 0
            context.Emit(OpCodes.Dup);

            EmitDataLoadRn(context);

            context.Emit(OpCodes.Xor);

            EmitDataLoadOpers(context);

            context.Emit(OpCodes.Xor);
            context.Emit(OpCodes.And);

            context.EmitLdc_I(0);

            context.Emit(OpCodes.Clt);

            context.EmitStflg((int)APState.VBit);
        }

        public static void EmitDataLoadRm(AILEmitterCtx context)
        {
            context.EmitLdintzr(((IAOpCodeAluRs)context.CurrOp).Rm);
        }

        public static void EmitDataLoadOpers(AILEmitterCtx context)
        {
            EmitDataLoadRn(context);
            EmitDataLoadOper2(context);
        }

        public static void EmitDataLoadRn(AILEmitterCtx context)
        {
            IAOpCodeAlu op = (IAOpCodeAlu)context.CurrOp;

            if (op.DataOp == ADataOp.Logical || op is IAOpCodeAluRs)
                context.EmitLdintzr(op.Rn);
            else
                context.EmitLdint(op.Rn);
        }

        public static void EmitDataLoadOper2(AILEmitterCtx context)
        {
            switch (context.CurrOp)
            {
                case IAOpCodeAluImm op:
                    context.EmitLdc_I(op.Imm);
                    break;

                case IAOpCodeAluRs op:
                    context.EmitLdintzr(op.Rm);

                    switch (op.ShiftType)
                    {
                        case AShiftType.Lsl: context.EmitLsl(op.Shift); break;
                        case AShiftType.Lsr: context.EmitLsr(op.Shift); break;
                        case AShiftType.Asr: context.EmitAsr(op.Shift); break;
                        case AShiftType.Ror: context.EmitRor(op.Shift); break;
                    }
                    break;

                case IAOpCodeAluRx op:
                    context.EmitLdintzr(op.Rm);
                    context.EmitCast(op.IntType);
                    context.EmitLsl(op.Shift);
                    break;
            }
        }

        public static void EmitDataStore(AILEmitterCtx context)
        {
            EmitDataStore(context, false);
        }

        public static void EmitDataStoreS(AILEmitterCtx context)
        {
            EmitDataStore(context, true);
        }

        public static void EmitDataStore(AILEmitterCtx context, bool setFlags)
        {
            IAOpCodeAlu op = (IAOpCodeAlu)context.CurrOp;

            if (setFlags || op is IAOpCodeAluRs)
                context.EmitStintzr(op.Rd);
            else
                context.EmitStint(op.Rd);
        }

        public static void EmitSetNzcv(AILEmitterCtx context, int nzcv)
        {
            context.EmitLdc_I4((nzcv >> 0) & 1);

            context.EmitStflg((int)APState.VBit);

            context.EmitLdc_I4((nzcv >> 1) & 1);

            context.EmitStflg((int)APState.CBit);

            context.EmitLdc_I4((nzcv >> 2) & 1);

            context.EmitStflg((int)APState.ZBit);

            context.EmitLdc_I4((nzcv >> 3) & 1);

            context.EmitStflg((int)APState.NBit);
        }
    }
}