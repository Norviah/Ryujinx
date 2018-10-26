using ChocolArm64.State;
using System.Reflection.Emit;

namespace ChocolArm64.Translation
{
    internal struct AILOpCodeStore : IAilEmit
    {
        public int Index { get; private set; }

        public AIoType IoType { get; private set; }

        public ARegisterSize RegisterSize { get; private set; }

        public AILOpCodeStore(int index, AIoType ioType, ARegisterSize registerSize = 0)
        {
            this.Index        = index;
            this.IoType       = ioType;
            this.RegisterSize = registerSize;
        }

        public void Emit(AILEmitter context)
        {
            switch (IoType)
            {
                case AIoType.Arg: context.Generator.EmitStarg(Index); break;

                case AIoType.Fields:
                {
                    long intOutputs = context.LocalAlloc.GetIntOutputs(context.GetIlBlock(Index));
                    long vecOutputs = context.LocalAlloc.GetVecOutputs(context.GetIlBlock(Index));

                    StoreLocals(context, intOutputs, ARegisterType.Int);
                    StoreLocals(context, vecOutputs, ARegisterType.Vector);
                    
                    break;
                }

                case AIoType.Flag:   EmitStloc(context, Index, ARegisterType.Flag);   break;
                case AIoType.Int:    EmitStloc(context, Index, ARegisterType.Int);    break;
                case AIoType.Vector: EmitStloc(context, Index, ARegisterType.Vector); break;
            }
        }

        private void StoreLocals(AILEmitter context, long outputs, ARegisterType baseType)
        {
            for (int bit = 0; bit < 64; bit++)
            {
                long mask = 1L << bit;

                if ((outputs & mask) != 0)
                {
                    ARegister reg = AILEmitter.GetRegFromBit(bit, baseType);

                    context.Generator.EmitLdarg(ATranslatedSub.StateArgIdx);
                    context.Generator.EmitLdloc(context.GetLocalIndex(reg));

                    context.Generator.Emit(OpCodes.Stfld, reg.GetField());
                }
            }
        }

        private void EmitStloc(AILEmitter context, int index, ARegisterType registerType)
        {
            ARegister reg = new ARegister(index, registerType);

            if (registerType == ARegisterType.Int &&
                RegisterSize == ARegisterSize.Int32)
                context.Generator.Emit(OpCodes.Conv_U8);

            context.Generator.EmitStloc(context.GetLocalIndex(reg));
        }
    }
}