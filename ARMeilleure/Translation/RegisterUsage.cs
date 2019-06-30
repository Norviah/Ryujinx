using ARMeilleure.IntermediateRepresentation;
using ARMeilleure.State;
using System;

using static ARMeilleure.IntermediateRepresentation.OperandHelper;

namespace ARMeilleure.Translation
{
    static class RegisterUsage
    {
        private const long CallerSavedIntRegistersMask = 0x7fL  << 9;
        private const long PStateNzcvFlagsMask         = 0xfL   << 60;

        private const long CallerSavedVecRegistersMask = 0xffffL << 16;

        private const int RegsCount = 32;
        private const int RegsMask  = RegsCount - 1;

        private struct RegisterMask : IEquatable<RegisterMask>
        {
            public long IntMask { get; set; }
            public long VecMask { get; set; }

            public RegisterMask(long intMask, long vecMask)
            {
                IntMask = intMask;
                VecMask = vecMask;
            }

            public static RegisterMask operator &(RegisterMask x, RegisterMask y)
            {
                return new RegisterMask(x.IntMask & y.IntMask, x.VecMask & y.VecMask);
            }

            public static RegisterMask operator |(RegisterMask x, RegisterMask y)
            {
                return new RegisterMask(x.IntMask | y.IntMask, x.VecMask | y.VecMask);
            }

            public static RegisterMask operator ~(RegisterMask x)
            {
                return new RegisterMask(~x.IntMask, ~x.VecMask);
            }

            public static bool operator ==(RegisterMask x, RegisterMask y)
            {
                return x.Equals(y);
            }

            public static bool operator !=(RegisterMask x, RegisterMask y)
            {
                return !x.Equals(y);
            }

            public override bool Equals(object obj)
            {
                return obj is RegisterMask regMask && Equals(regMask);
            }

            public bool Equals(RegisterMask other)
            {
                return IntMask == other.IntMask && VecMask == other.VecMask;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(IntMask, VecMask);
            }
        }

        public static void RunPass(ControlFlowGraph cfg)
        {
            //Computer local register inputs and outputs used inside blocks.
            RegisterMask[] localInputs  = new RegisterMask[cfg.Blocks.Count];
            RegisterMask[] localOutputs = new RegisterMask[cfg.Blocks.Count];

            foreach (BasicBlock block in cfg.Blocks)
            {
                foreach (Node node in block.Operations)
                {
                    Operation operation = node as Operation;

                    for (int srcIndex = 0; srcIndex < operation.SourcesCount; srcIndex++)
                    {
                        Operand source = operation.GetSource(srcIndex);

                        if (source.Kind != OperandKind.Register)
                        {
                            continue;
                        }

                        Register register = source.GetRegister();

                        localInputs[block.Index] |= GetMask(register) & ~localOutputs[block.Index];
                    }

                    if (operation.Dest != null && operation.Dest.Kind == OperandKind.Register)
                    {
                        localOutputs[block.Index] |= GetMask(operation.Dest.GetRegister());
                    }
                }
            }

            //Compute global register inputs and outputs used across blocks.
            RegisterMask[] globalCmnOutputs = new RegisterMask[cfg.Blocks.Count];

            RegisterMask[] globalInputs  = new RegisterMask[cfg.Blocks.Count];
            RegisterMask[] globalOutputs = new RegisterMask[cfg.Blocks.Count];

            bool modified;

            bool firstPass = true;

            do
            {
                modified = false;

                //Compute register outputs.
                for (int index = cfg.PostOrderBlocks.Length - 1; index >= 0; index--)
                {
                    BasicBlock block = cfg.PostOrderBlocks[index];

                    if (block.Predecessors.Count != 0 && !HasContextLoad(block))
                    {
                        BasicBlock predecessor = block.Predecessors[0];

                        RegisterMask cmnOutputs = localOutputs[predecessor.Index] | globalCmnOutputs[predecessor.Index];

                        RegisterMask outputs = globalOutputs[predecessor.Index];

                        for (int pIndex = 1; pIndex < block.Predecessors.Count; pIndex++)
                        {
                            predecessor = block.Predecessors[pIndex];

                            cmnOutputs &= localOutputs[predecessor.Index] | globalCmnOutputs[predecessor.Index];

                            outputs |= globalOutputs[predecessor.Index];
                        }

                        globalInputs[block.Index] |= outputs & ~cmnOutputs;

                        if (!firstPass)
                        {
                            cmnOutputs &= globalCmnOutputs[block.Index];
                        }

                        if (Exchange(globalCmnOutputs, block.Index, cmnOutputs))
                        {
                            modified = true;
                        }

                        outputs |= localOutputs[block.Index];

                        if (Exchange(globalOutputs, block.Index, globalOutputs[block.Index] | outputs))
                        {
                            modified = true;
                        }
                    }
                    else if (Exchange(globalOutputs, block.Index, localOutputs[block.Index]))
                    {
                        modified = true;
                    }
                }

                //Compute register inputs.
                for (int index = 0; index < cfg.PostOrderBlocks.Length; index++)
                {
                    BasicBlock block = cfg.PostOrderBlocks[index];

                    RegisterMask inputs = localInputs[block.Index];

                    if (block.Next != null)
                    {
                        inputs |= globalInputs[block.Next.Index];
                    }

                    if (block.Branch != null)
                    {
                        inputs |= globalInputs[block.Branch.Index];
                    }

                    inputs &= ~globalCmnOutputs[block.Index];

                    if (Exchange(globalInputs, block.Index, globalInputs[block.Index] | inputs))
                    {
                        modified = true;
                    }
                }

                firstPass = false;
            }
            while (modified);

            //Insert load and store context instructions where needed.
            foreach (BasicBlock block in cfg.Blocks)
            {
                bool hasContextLoad = HasContextLoad(block);

                if (hasContextLoad)
                {
                    block.Operations.RemoveFirst();
                }

                //The only block without any predecessor should be the entry block.
                //It always needs a context load as it is the first block to run.
                if (block.Predecessors.Count == 0 || hasContextLoad)
                {
                    LoadLocals(block, globalInputs[block.Index].VecMask, RegisterType.Vector);
                    LoadLocals(block, globalInputs[block.Index].IntMask, RegisterType.Integer);
                }

                bool hasContextStore = HasContextStore(block);

                if (hasContextStore)
                {
                    block.Operations.RemoveLast();
                }

                if (EndsWithReturn(block) || hasContextStore)
                {
                    StoreLocals(block, globalOutputs[block.Index].IntMask, RegisterType.Integer);
                    StoreLocals(block, globalOutputs[block.Index].VecMask, RegisterType.Vector);
                }
            }
        }

        private static bool HasContextLoad(BasicBlock block)
        {
            return StartsWith(block, Instruction.LoadFromContext) && block.Operations.First.Value.SourcesCount == 0;
        }

        private static bool HasContextStore(BasicBlock block)
        {
            return EndsWith(block, Instruction.StoreToContext) && block.GetLastOp().SourcesCount == 0;
        }

        private static bool StartsWith(BasicBlock block, Instruction inst)
        {
            if (block.Operations.Count == 0)
            {
                return false;
            }

            return block.Operations.First.Value is Operation operation && operation.Inst == inst;
        }

        private static bool EndsWith(BasicBlock block, Instruction inst)
        {
            if (block.Operations.Count == 0)
            {
                return false;
            }

            return block.Operations.Last.Value is Operation operation && operation.Inst == inst;
        }

        private static RegisterMask GetMask(Register register)
        {
            long intMask = 0;
            long vecMask = 0;

            switch (register.Type)
            {
                case RegisterType.Flag:    intMask = (1L << RegsCount) << register.Index; break;
                case RegisterType.Integer: intMask =  1L               << register.Index; break;
                case RegisterType.Vector:  vecMask =  1L               << register.Index; break;
            }

            return new RegisterMask(intMask, vecMask);
        }

        private static bool Exchange(RegisterMask[] masks, int blkIndex, RegisterMask value)
        {
            RegisterMask oldValue = masks[blkIndex];

            masks[blkIndex] = value;

            return oldValue != value;
        }

        private static void LoadLocals(BasicBlock block, long inputs, RegisterType baseType)
        {
            for (int bit = 63; bit >= 0; bit--)
            {
                long mask = 1L << bit;

                if ((inputs & mask) == 0)
                {
                    continue;
                }

                Operand dest = GetRegFromBit(bit, baseType);

                int offset = NativeContext.GetRegisterOffset(dest.GetRegister());

                Operation operation = new Operation(Instruction.LoadFromContext, dest, Const(offset));

                block.Operations.AddFirst(operation);
            }
        }

        private static void StoreLocals(BasicBlock block, long outputs, RegisterType baseType)
        {
            if (Optimizations.AssumeStrictAbiCompliance)
            {
                if (baseType == RegisterType.Integer || baseType == RegisterType.Flag)
                {
                    outputs = ClearCallerSavedIntRegs(outputs);
                }
                else /* if (baseType == RegisterType.Vector) */
                {
                    outputs = ClearCallerSavedVecRegs(outputs);
                }
            }

            for (int bit = 0; bit < 64; bit++)
            {
                long mask = 1L << bit;

                if ((outputs & mask) == 0)
                {
                    continue;
                }

                Operand source = GetRegFromBit(bit, baseType);

                int offset = NativeContext.GetRegisterOffset(source.GetRegister());

                Operation operation = new Operation(Instruction.StoreToContext, null, Const(offset), source);

                block.Append(operation);
            }
        }

        private static Operand GetRegFromBit(int bit, RegisterType baseType)
        {
            if (bit < RegsCount)
            {
                return new Operand(bit, baseType, GetOperandType(baseType));
            }
            else if (baseType == RegisterType.Integer)
            {
                return new Operand(bit & RegsMask, RegisterType.Flag, OperandType.I32);
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(bit));
            }
        }

        private static OperandType GetOperandType(RegisterType type)
        {
            switch (type)
            {
                case RegisterType.Flag:    return OperandType.I32;
                case RegisterType.Integer: return OperandType.I64;
                case RegisterType.Vector:  return OperandType.V128;
            }

            throw new ArgumentException($"Invalid register type \"{type}\".");
        }

        private static bool EndsWithReturn(BasicBlock block)
        {
            if (!(block.GetLastOp() is Operation operation))
            {
                return false;
            }

            return operation.Inst == Instruction.Return;
        }

        private static long ClearCallerSavedIntRegs(long mask)
        {
            //TODO: ARM32 support.
            mask &= ~(CallerSavedIntRegistersMask | PStateNzcvFlagsMask);

            return mask;
        }

        private static long ClearCallerSavedVecRegs(long mask)
        {
            //TODO: ARM32 support.
            mask &= ~CallerSavedVecRegistersMask;

            return mask;
        }
    }
}