using System.Reflection.Emit;

namespace ChocolArm64.Translation
{
    internal struct AILOpCodeBranch : IAilEmit
    {
        private OpCode   _ilOp;
        private AILLabel _label;

        public AILOpCodeBranch(OpCode ilOp, AILLabel label)
        {
            this._ilOp  = ilOp;
            this._label = label;
        }

        public void Emit(AILEmitter context)
        {
            context.Generator.Emit(_ilOp, _label.GetLabel(context));
        }
    }
}