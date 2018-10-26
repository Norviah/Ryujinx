using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.SystemState;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ryujinx.HLE.HOS.Services.Set
{
    internal class ISystemSettingsServer : IpcService
    {
        private Dictionary<int, ServiceProcessRequest> _mCommands;

        public override IReadOnlyDictionary<int, ServiceProcessRequest> Commands => _mCommands;

        public ISystemSettingsServer()
        {
            _mCommands = new Dictionary<int, ServiceProcessRequest>()
            {
                { 4,  GetFirmwareVersion2  },
                { 23, GetColorSetId        },
                { 24, SetColorSetId        },
                { 38, GetSettingsItemValue }
            };
        }

        public static long GetFirmwareVersion2(ServiceCtx context)
        {
            long replyPos  = context.Request.RecvListBuff[0].Position;
            long replySize = context.Request.RecvListBuff[0].Size;

            const byte majorFwVersion = 0x03;
            const byte minorFwVersion = 0x00;
            const byte microFwVersion = 0x00;
            const byte unknown        = 0x00; //Build?

            const int revisionNumber = 0x0A;

            const string platform   = "NX";
            const string unknownHex = "7fbde2b0bba4d14107bf836e4643043d9f6c8e47";
            const string version    = "3.0.0";
            const string build      = "NintendoSDK Firmware for NX 3.0.0-10.0";

            //http://switchbrew.org/index.php?title=System_Version_Title
            using (MemoryStream ms = new MemoryStream(0x100))
            {
                BinaryWriter writer = new BinaryWriter(ms);

                writer.Write(majorFwVersion);
                writer.Write(minorFwVersion);
                writer.Write(microFwVersion);
                writer.Write(unknown);

                writer.Write(revisionNumber);

                writer.Write(Encoding.ASCII.GetBytes(platform));

                ms.Seek(0x28, SeekOrigin.Begin);

                writer.Write(Encoding.ASCII.GetBytes(unknownHex));

                ms.Seek(0x68, SeekOrigin.Begin);

                writer.Write(Encoding.ASCII.GetBytes(version));

                ms.Seek(0x80, SeekOrigin.Begin);

                writer.Write(Encoding.ASCII.GetBytes(build));

                context.Memory.WriteBytes(replyPos, ms.ToArray());
            }

            return 0;
        }

        public static long GetColorSetId(ServiceCtx context)
        {
            context.ResponseData.Write((int)context.Device.System.State.ThemeColor);

            return 0;
        }

        public static long SetColorSetId(ServiceCtx context)
        {
            int colorSetId = context.RequestData.ReadInt32();

            context.Device.System.State.ThemeColor = (ColorSet)colorSetId;

            return 0;
        }

        public static long GetSettingsItemValue(ServiceCtx context)
        {
            long classPos  = context.Request.PtrBuff[0].Position;
            long classSize = context.Request.PtrBuff[0].Size;

            long namePos  = context.Request.PtrBuff[1].Position;
            long nameSize = context.Request.PtrBuff[1].Size;

            long replyPos  = context.Request.ReceiveBuff[0].Position;
            long replySize = context.Request.ReceiveBuff[0].Size;

            byte[] Class = context.Memory.ReadBytes(classPos, classSize);
            byte[] name  = context.Memory.ReadBytes(namePos, nameSize);

            string askedSetting = Encoding.ASCII.GetString(Class).Trim('\0') + "!" + Encoding.ASCII.GetString(name).Trim('\0');

            NxSettings.Settings.TryGetValue(askedSetting, out object nxSetting);

            if (nxSetting != null)
            {
                byte[] settingBuffer = new byte[replySize];

                if (nxSetting is string stringValue)
                {
                    if (stringValue.Length + 1 > replySize)
                        Logger.PrintError(LogClass.ServiceSet, $"{askedSetting} String value size is too big!");
                    else
                        settingBuffer = Encoding.ASCII.GetBytes(stringValue + "\0");
                }

                if (nxSetting is int intValue)
                    settingBuffer = BitConverter.GetBytes(intValue);
                else if (nxSetting is bool boolValue)
                    settingBuffer[0] = boolValue ? (byte)1 : (byte)0;
                else
                    throw new NotImplementedException(nxSetting.GetType().Name);

                context.Memory.WriteBytes(replyPos, settingBuffer);

                Logger.PrintDebug(LogClass.ServiceSet, $"{askedSetting} set value: {nxSetting} as {nxSetting.GetType()}");
            }
            else
            {
                Logger.PrintError(LogClass.ServiceSet, $"{askedSetting} not found!");
            }

            return 0;
        }
    }
}
