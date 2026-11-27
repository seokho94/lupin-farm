using System;
using System.Threading;
using LupinFarm.Core;

namespace LupinFarm.Ui
{
    /// <summary>전역 단축키 스레드: RegisterHotKey + 메시지 루프 (03 8절).</summary>
    internal sealed class BossKey
    {
        const int HotkeyId = 0x4C46; // "LF"

        Thread thread;
        uint threadId;
        int pending;

        public string Active { get; private set; }

        public bool Start(string comboText, out string error)
        {
            error = null;
            Stop();
            KeyCombo combo;
            if (!KeyCombo.TryParse(comboText, out combo))
            {
                error = comboText;
                return false;
            }

            bool ok = false;
            var ready = new ManualResetEvent(false);
            thread = new Thread(delegate()
            {
                try
                {
                    Native.MSG msg;
                    Native.PeekMessageW(out msg, IntPtr.Zero, 0, 0, Native.PM_NOREMOVE);
                    threadId = Native.GetCurrentThreadId();
                    ok = Native.RegisterHotKey(IntPtr.Zero, HotkeyId, combo.Mods | Native.MOD_NOREPEAT, combo.Vk);
                    ready.Set();
                    if (!ok) return;
                    while (Native.GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0)
                    {
                        if (msg.message == Native.WM_HOTKEY) Interlocked.Increment(ref pending);
                    }
                    Native.UnregisterHotKey(IntPtr.Zero, HotkeyId);
                }
                catch (Exception)
                {
                    ready.Set();
                }
            });
            thread.IsBackground = true;
            thread.Name = "bosskey";
            thread.Start();
            ready.WaitOne(2000);
            if (!ok)
            {
                thread = null;
                error = combo.Display;
                return false;
            }
            Active = combo.Display;
            return true;
        }

        public void Stop()
        {
            if (thread == null) return;
            Native.PostThreadMessageW(threadId, Native.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            thread.Join(500);
            thread = null;
            Active = null;
        }

        /// <summary>눌린 횟수가 홀수면 한 번 전환한다.</summary>
        public bool TakeToggle()
        {
            int n = Interlocked.Exchange(ref pending, 0);
            return n % 2 == 1;
        }
    }
}
