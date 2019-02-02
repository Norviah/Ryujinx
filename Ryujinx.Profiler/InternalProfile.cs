﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ryujinx.Common;

namespace Ryujinx.Profiler
{
    public class InternalProfile
    {
        private struct TimerQueueValue
        {
            public ProfileConfig Config;
            public long Time;
            public bool IsBegin;
        }

        internal Dictionary<ProfileConfig, TimingInfo> Timers { get; set; }

        private readonly object _timerQueueClearLock = new object();
        private ConcurrentQueue<TimerQueueValue> _timerQueue;

        private readonly object _sessionLock = new object();
        private int _sessionCounter = 0;

        // Cleanup thread
        private readonly Thread _cleanupThread;
        private bool _cleanupRunning;
        private readonly long _history;
        private long _preserve;

        // Timing flags
        private TimingFlag[] _timingFlags;
        private int _timingFlagCount;
        private int _timingFlagIndex;

        private const int MaxFlags = 500;

        private Action<TimingFlag> _timingFlagCallback;

        public InternalProfile(long history)
        {
            Timers          = new Dictionary<ProfileConfig, TimingInfo>();
            _timingFlags    = new TimingFlag[MaxFlags];
            _timerQueue     = new ConcurrentQueue<TimerQueueValue>();
            _history        = history;
            _cleanupRunning = true;

            // Create cleanup thread.
            _cleanupThread = new Thread(CleanupLoop);
            _cleanupThread.Start();
        }

        private void CleanupLoop()
        {
            bool queueCleared = false;

            while (_cleanupRunning)
            {
                // Ensure we only ever have 1 instance modifying timers or timerQueue
                if (Monitor.TryEnter(_timerQueueClearLock))
                {
                    queueCleared = ClearTimerQueue();

                    // Calculate before foreach to mitigate redundant calculations
                    long cleanupBefore = PerformanceCounter.ElapsedTicks - _history;
                    long preserveStart = _preserve - _history;

                    // Each cleanup is self contained so run in parallel for maximum efficiency
                    Parallel.ForEach(Timers, (t) => t.Value.Cleanup(cleanupBefore, preserveStart, _preserve));

                    Monitor.Exit(_timerQueueClearLock);
                }

                // Only sleep if queue was sucessfully cleared
                if (queueCleared)
                {
                    Thread.Sleep(5);
                }
            }
        }

        private bool ClearTimerQueue()
        {
            int count = 0;

            while (_timerQueue.TryDequeue(out var item))
            {
                if (!Timers.TryGetValue(item.Config, out var value))
                {
                    value = new TimingInfo();
                    Timers.Add(item.Config, value);
                }

                if (item.IsBegin)
                {
                    value.Begin(item.Time);
                }
                else
                {
                    value.End(item.Time);
                }

                // Don't block for too long as memory disposal is blocked while this function runs
                if (count++ > 10000)
                {
                    return false;
                }
            }

            return true;
        }

        public void FlagTime(TimingFlagType flagType)
        {
            _timingFlags[_timingFlagIndex] = new TimingFlag()
            {
                FlagType  = flagType,
                Timestamp = PerformanceCounter.ElapsedTicks
            };

            if (++_timingFlagIndex >= MaxFlags)
            {
                _timingFlagIndex = 0;
            }

            _timingFlagCount = Math.Max(_timingFlagCount + 1, MaxFlags);

            _timingFlagCallback?.Invoke(_timingFlags[_timingFlagIndex]);
        }

        public void BeginProfile(ProfileConfig config)
        {
            _timerQueue.Enqueue(new TimerQueueValue()
            {
                Config  = config,
                IsBegin = true,
                Time    = PerformanceCounter.ElapsedTicks,
            });
        }

        public void EndProfile(ProfileConfig config)
        {
            _timerQueue.Enqueue(new TimerQueueValue()
            {
                Config  = config,
                IsBegin = false,
                Time    = PerformanceCounter.ElapsedTicks,
            });
        }

        public string GetSession()
        {
            // Can be called from multiple threads so locked to ensure no duplicate sessions are generated
            lock (_sessionLock)
            {
                return (_sessionCounter++).ToString();
            }
        }

        public Dictionary<ProfileConfig, TimingInfo> GetProfilingData()
        {
            _preserve = PerformanceCounter.ElapsedTicks;

            // Skip clearing queue if already clearing
            if (Monitor.TryEnter(_timerQueueClearLock))
            {
                ClearTimerQueue();
                Monitor.Exit(_timerQueueClearLock);
            }

            return Timers;
        }

        public TimingFlag[] GetTimingFlags()
        {
            int count = Math.Max(_timingFlagCount, MaxFlags);
            TimingFlag[] outFlags = new TimingFlag[count];
            
            for (int i = 0, sourceIndex = _timingFlagIndex; i < count; i++, sourceIndex++)
            {
                if (sourceIndex >= MaxFlags)
                    sourceIndex = 0;
                outFlags[i] = _timingFlags[sourceIndex];
            }

            return outFlags;
        }

        public void RegisterFlagReciever(Action<TimingFlag> reciever)
        {
            _timingFlagCallback = reciever;
        }

        public void Dispose()
        {
            _cleanupRunning = false;
            _cleanupThread.Join();
        }
    }
}
