namespace Ryujinx.HLE.HOS.Services.Nv.NvHostCtrl
{
    internal class NvHostCtrlUserCtx
    {
        public const int LocksCount  = 16;
        public const int EventsCount = 64;

        public NvHostSyncpt Syncpt { get; private set; }

        public NvHostEvent[] Events { get; private set; }

        public NvHostCtrlUserCtx()
        {
            Syncpt = new NvHostSyncpt();

            Events = new NvHostEvent[EventsCount];
        }
    }
}