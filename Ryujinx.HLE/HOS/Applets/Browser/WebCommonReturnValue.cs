﻿namespace Ryujinx.HLE.HOS.Applets.Browser
{
    unsafe struct WebCommonReturnValue
    {
        public WebExitReason ExitReason;
        public uint          Padding;
        public fixed byte    LastUrl[0x1000];
        public ulong         LastUrlSize;
    }
}
