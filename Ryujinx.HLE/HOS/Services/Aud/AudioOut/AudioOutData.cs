using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Aud.AudioOut
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioOutData
    {
        public long NextBufferPtr;
        public long SampleBufferPtr;
        public long SampleBufferCapacity;
        public long SampleBufferSize;
        public long SampleBufferInnerOffset;
    }
}