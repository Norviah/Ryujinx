using ChocolArm64.Memory;
using Ryujinx.Audio;
using Ryujinx.Audio.Adpcm;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Ipc;
using Ryujinx.HLE.HOS.Kernel;
using Ryujinx.HLE.Utilities;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Ryujinx.HLE.HOS.Services.Aud.AudioRenderer
{
    internal class IAudioRenderer : IpcService, IDisposable
    {
        //This is the amount of samples that are going to be appended
        //each time that RequestUpdateAudioRenderer is called. Ideally,
        //this value shouldn't be neither too small (to avoid the player
        //starving due to running out of samples) or too large (to avoid
        //high latency).
        private const int MixBufferSamplesCount = 960;

        private Dictionary<int, ServiceProcessRequest> _mCommands;

        public override IReadOnlyDictionary<int, ServiceProcessRequest> Commands => _mCommands;

        private KEvent _updateEvent;

        private AMemory _memory;

        private IAalOutput _audioOut;

        private AudioRendererParameter _params;

        private MemoryPoolContext[] _memoryPools;

        private VoiceContext[] _voices;

        private int _track;

        private PlayState _playState;

        public IAudioRenderer(
            Horizon                system,
            AMemory                memory,
            IAalOutput             audioOut,
            AudioRendererParameter Params)
        {
            _mCommands = new Dictionary<int, ServiceProcessRequest>()
            {
                { 0, GetSampleRate              },
                { 1, GetSampleCount             },
                { 2, GetMixBufferCount          },
                { 3, GetState                   },
                { 4, RequestUpdateAudioRenderer },
                { 5, StartAudioRenderer         },
                { 6, StopAudioRenderer          },
                { 7, QuerySystemEvent           }
            };

            _updateEvent = new KEvent(system);

            this._memory   = memory;
            this._audioOut = audioOut;
            this._params   = Params;

            _track = audioOut.OpenTrack(
                AudioConsts.HostSampleRate,
                AudioConsts.HostChannelsCount,
                AudioCallback);

            _memoryPools = CreateArray<MemoryPoolContext>(Params.EffectCount + Params.VoiceCount * 4);

            _voices = CreateArray<VoiceContext>(Params.VoiceCount);

            InitializeAudioOut();

            _playState = PlayState.Stopped;
        }

        //  GetSampleRate() -> u32
        public long GetSampleRate(ServiceCtx context)
        {
            context.ResponseData.Write(_params.SampleRate);

            return 0;
        }

        //  GetSampleCount() -> u32
        public long GetSampleCount(ServiceCtx context)
        {
            context.ResponseData.Write(_params.SampleCount);

            return 0;
        }

        // GetMixBufferCount() -> u32
        public long GetMixBufferCount(ServiceCtx context)
        {
            context.ResponseData.Write(_params.MixCount);

            return 0;
        }

        // GetState() -> u32
        private long GetState(ServiceCtx context)
        {
            context.ResponseData.Write((int)_playState);

            Logger.PrintStub(LogClass.ServiceAudio, $"Stubbed. Renderer State: {Enum.GetName(typeof(PlayState), _playState)}");

            return 0;
        }

        private void AudioCallback()
        {
            _updateEvent.ReadableEvent.Signal();
        }

        private static T[] CreateArray<T>(int size) where T : new()
        {
            T[] output = new T[size];

            for (int index = 0; index < size; index++) output[index] = new T();

            return output;
        }

        private void InitializeAudioOut()
        {
            AppendMixedBuffer(0);
            AppendMixedBuffer(1);
            AppendMixedBuffer(2);

            _audioOut.Start(_track);
        }

        public long RequestUpdateAudioRenderer(ServiceCtx context)
        {
            long outputPosition = context.Request.ReceiveBuff[0].Position;
            long outputSize     = context.Request.ReceiveBuff[0].Size;

            AMemoryHelper.FillWithZeros(context.Memory, outputPosition, (int)outputSize);

            long inputPosition = context.Request.SendBuff[0].Position;

            StructReader reader = new StructReader(context.Memory, inputPosition);
            StructWriter writer = new StructWriter(context.Memory, outputPosition);

            UpdateDataHeader inputHeader = reader.Read<UpdateDataHeader>();

            reader.Read<BehaviorIn>(inputHeader.BehaviorSize);

            MemoryPoolIn[] memoryPoolsIn = reader.Read<MemoryPoolIn>(inputHeader.MemoryPoolSize);

            for (int index = 0; index < memoryPoolsIn.Length; index++)
            {
                MemoryPoolIn memoryPool = memoryPoolsIn[index];

                if (memoryPool.State == MemoryPoolState.RequestAttach)
                    _memoryPools[index].OutStatus.State = MemoryPoolState.Attached;
                else if (memoryPool.State == MemoryPoolState.RequestDetach) _memoryPools[index].OutStatus.State = MemoryPoolState.Detached;
            }

            reader.Read<VoiceChannelResourceIn>(inputHeader.VoiceResourceSize);

            VoiceIn[] voicesIn = reader.Read<VoiceIn>(inputHeader.VoiceSize);

            for (int index = 0; index < voicesIn.Length; index++)
            {
                VoiceIn voice = voicesIn[index];

                VoiceContext voiceCtx = _voices[index];

                voiceCtx.SetAcquireState(voice.Acquired != 0);

                if (voice.Acquired == 0) continue;

                if (voice.FirstUpdate != 0)
                {
                    voiceCtx.AdpcmCtx = GetAdpcmDecoderContext(
                        voice.AdpcmCoeffsPosition,
                        voice.AdpcmCoeffsSize);

                    voiceCtx.SampleFormat  = voice.SampleFormat;
                    voiceCtx.SampleRate    = voice.SampleRate;
                    voiceCtx.ChannelsCount = voice.ChannelsCount;

                    voiceCtx.SetBufferIndex(voice.BaseWaveBufferIndex);
                }

                voiceCtx.WaveBuffers[0] = voice.WaveBuffer0;
                voiceCtx.WaveBuffers[1] = voice.WaveBuffer1;
                voiceCtx.WaveBuffers[2] = voice.WaveBuffer2;
                voiceCtx.WaveBuffers[3] = voice.WaveBuffer3;
                voiceCtx.Volume         = voice.Volume;
                voiceCtx.PlayState      = voice.PlayState;
            }

            UpdateAudio();

            UpdateDataHeader outputHeader = new UpdateDataHeader();

            int updateHeaderSize = Marshal.SizeOf<UpdateDataHeader>();

            outputHeader.Revision               = IAudioRendererManager.RevMagic;
            outputHeader.BehaviorSize           = 0xb0;
            outputHeader.MemoryPoolSize         = (_params.EffectCount + _params.VoiceCount * 4) * 0x10;
            outputHeader.VoiceSize              = _params.VoiceCount  * 0x10;
            outputHeader.EffectSize             = _params.EffectCount * 0x10;
            outputHeader.SinkSize               = _params.SinkCount   * 0x20;
            outputHeader.PerformanceManagerSize = 0x10;
            outputHeader.TotalSize              = updateHeaderSize             +
                                                  outputHeader.BehaviorSize    +
                                                  outputHeader.MemoryPoolSize +
                                                  outputHeader.VoiceSize      +
                                                  outputHeader.EffectSize     +
                                                  outputHeader.SinkSize       +
                                                  outputHeader.PerformanceManagerSize;

            writer.Write(outputHeader);

            foreach (MemoryPoolContext memoryPool in _memoryPools) writer.Write(memoryPool.OutStatus);

            foreach (VoiceContext voice in _voices) writer.Write(voice.OutStatus);

            return 0;
        }

        public long StartAudioRenderer(ServiceCtx context)
        {
            Logger.PrintStub(LogClass.ServiceAudio, "Stubbed.");

            _playState = PlayState.Playing;

            return 0;
        }

        public long StopAudioRenderer(ServiceCtx context)
        {
            Logger.PrintStub(LogClass.ServiceAudio, "Stubbed.");

            _playState = PlayState.Stopped;

            return 0;
        }

        public long QuerySystemEvent(ServiceCtx context)
        {
            if (context.Process.HandleTable.GenerateHandle(_updateEvent.ReadableEvent, out int handle) != KernelResult.Success) throw new InvalidOperationException("Out of handles!");

            context.Response.HandleDesc = IpcHandleDesc.MakeCopy(handle);

            return 0;
        }

        private AdpcmDecoderContext GetAdpcmDecoderContext(long position, long size)
        {
            if (size == 0) return null;

            AdpcmDecoderContext context = new AdpcmDecoderContext();

            context.Coefficients = new short[size >> 1];

            for (int offset = 0; offset < size; offset += 2) context.Coefficients[offset >> 1] = _memory.ReadInt16(position + offset);

            return context;
        }

        private void UpdateAudio()
        {
            long[] released = _audioOut.GetReleasedBuffers(_track, 2);

            for (int index = 0; index < released.Length; index++) AppendMixedBuffer(released[index]);
        }

        private void AppendMixedBuffer(long tag)
        {
            int[] mixBuffer = new int[MixBufferSamplesCount * AudioConsts.HostChannelsCount];

            foreach (VoiceContext voice in _voices)
            {
                if (!voice.Playing) continue;

                int outOffset = 0;

                int pendingSamples = MixBufferSamplesCount;

                while (pendingSamples > 0)
                {
                    int[] samples = voice.GetBufferData(_memory, pendingSamples, out int returnedSamples);

                    if (returnedSamples == 0) break;

                    pendingSamples -= returnedSamples;

                    for (int offset = 0; offset < samples.Length; offset++)
                    {
                        int sample = (int)(samples[offset] * voice.Volume);

                        mixBuffer[outOffset++] += sample;
                    }
                }
            }

            _audioOut.AppendBuffer(_track, tag, GetFinalBuffer(mixBuffer));
        }

        private static short[] GetFinalBuffer(int[] buffer)
        {
            short[] output = new short[buffer.Length];

            for (int offset = 0; offset < buffer.Length; offset++) output[offset] = DspUtils.Saturate(buffer[offset]);

            return output;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) _audioOut.CloseTrack(_track);
        }
    }
}
