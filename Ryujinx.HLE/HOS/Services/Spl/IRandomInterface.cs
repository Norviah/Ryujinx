﻿using System;
using System.Security.Cryptography;

namespace Ryujinx.HLE.HOS.Services.Spl
{
    [Service("csrng")]
    class IRandomInterface : IpcService, IDisposable
    {
        private RNGCryptoServiceProvider _rng;

        public IRandomInterface(ServiceCtx context)
        {
            _rng = new RNGCryptoServiceProvider();
        }

        [Command(0)]
        // GetRandomBytes() -> buffer<unknown, 6>
        public long GetRandomBytes(ServiceCtx context)
        {
            byte[] randomBytes = new byte[context.Request.ReceiveBuff[0].Size];

            _rng.GetBytes(randomBytes);

            context.Memory.WriteBytes(context.Request.ReceiveBuff[0].Position, randomBytes);

            return 0;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _rng.Dispose();
            }
        }
    }
}