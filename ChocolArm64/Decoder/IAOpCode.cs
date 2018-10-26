using ChocolArm64.Instruction;
using ChocolArm64.State;

namespace ChocolArm64.Decoder
{
    internal interface IAOpCode
    {
        long Position { get; }

        AInstEmitter  Emitter      { get; }
        ARegisterSize RegisterSize { get; }
    }
}