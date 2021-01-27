using Ryujinx.HLE.HOS.Tamper.Operations;

namespace Ryujinx.HLE.HOS.Tamper.Conditions
{
    internal class InputMask : ICondition
    {
        private long _mask;
        private Parameter<long> _input;

        public InputMask(long mask, Parameter<long> input)
        {
            _mask = mask;
            _input = input;
        }

        public bool Evaluate()
        {
            return (_input.Value & _mask) != 0;
        }
    }
}
