using System.Threading;

namespace Ryujinx.HLE.HOS.Services.Nv.NvMap
{
    internal class NvMapHandle
    {
        public int  Handle;
        public int  Id;
        public int  Size;
        public int  Align;
        public int  Kind;
        public long Address;
        public bool Allocated;

        private long _dupes;

        public NvMapHandle()
        {
            _dupes = 1;
        }

        public NvMapHandle(int size) : this()
        {
            this.Size = size;
        }

        public void IncrementRefCount()
        {
            Interlocked.Increment(ref _dupes);
        }

        public long DecrementRefCount()
        {
            return Interlocked.Decrement(ref _dupes);
        }
    }
}