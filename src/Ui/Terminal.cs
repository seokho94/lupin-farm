using System;

namespace LupinFarm.Ui
{
    /// <summary>콘솔 준비와 복구, VT 출력, 대체 화면, 탭 제목, 창 크기 (03 5.4절).</summary>
    internal static class Terminal
    {
        public const string Esc = "\u001b";

        public static IntPtr In;
        public static IntPtr Out;
        static uint origIn;
        static uint origOut;
        static bool inited;
        static bool inAlt;
        static string origTitle;

        public static bool InAlt
        {
            get { return inAlt; }
        }

        public static bool Init(out string error)
        {
            error = null;
            In = Native.GetStdHandle(Native.STD_INPUT_HANDLE);
            Out = Native.GetStdHandle(Native.STD_OUTPUT_HANDLE);
            if (!Native.GetConsoleMode(In, out origIn) || !Native.GetConsoleMode(Out, out origOut))
            {
                error = "Lupin Farm needs a console. Run bin\\lupin.exe in a Windows Terminal (PowerShell) tab.";
                return false;
            }

            uint inMode = origIn | Native.ENABLE_WINDOW_INPUT | Native.ENABLE_MOUSE_INPUT | Native.ENABLE_EXTENDED_FLAGS;
            inMode &= ~(Native.ENABLE_PROCESSED_INPUT | Native.ENABLE_LINE_INPUT | Native.ENABLE_ECHO_INPUT
                        | Native.ENABLE_QUICK_EDIT_MODE | Native.ENABLE_VIRTUAL_TERMINAL_INPUT);
            Native.SetConsoleMode(In, inMode);

            uint outMode = origOut | Native.ENABLE_PROCESSED_OUTPUT | Native.ENABLE_VIRTUAL_TERMINAL_PROCESSING | Native.DISABLE_NEWLINE_AUTO_RETURN;
            if (!Native.SetConsoleMode(Out, outMode))
            {
                outMode = origOut | Native.ENABLE_PROCESSED_OUTPUT | Native.ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                if (!Native.SetConsoleMode(Out, outMode))
                {
                    Native.SetConsoleMode(In, origIn);
                    error = "This console does not support VT sequences. Use Windows Terminal.";
                    return false;
                }
            }
            Native.SetConsoleOutputCP(65001);
            try
            {
                origTitle = Console.Title;
            }
            catch (Exception)
            {
                origTitle = null;
            }
            inited = true;
            return true;
        }

        public static void Write(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            int off = 0;
            while (off < s.Length)
            {
                int n = Math.Min(8192, s.Length - off);
                if (off + n < s.Length && char.IsHighSurrogate(s[off + n - 1])) n--;
                uint written;
                Native.WriteConsoleW(Out, s.Substring(off, n), (uint)n, out written, IntPtr.Zero);
                off += n;
            }
        }

        public static void EnterAlt()
        {
            Write(Esc + "[?1049h" + Esc + "[H" + Esc + "[2J");
            inAlt = true;
        }

        public static void LeaveAlt()
        {
            Write(Esc + "[0m" + Esc + "[?1049l" + Esc + "[?25h");
            inAlt = false;
        }

        /// <summary>창(보이는 영역) 크기와 버퍼 안 창의 위쪽 줄.</summary>
        public static bool GetSize(out int width, out int height, out int top)
        {
            Native.CONSOLE_SCREEN_BUFFER_INFO info;
            if (Native.GetConsoleScreenBufferInfo(Out, out info))
            {
                width = info.srWindow.Right - info.srWindow.Left + 1;
                height = info.srWindow.Bottom - info.srWindow.Top + 1;
                top = info.srWindow.Top;
                return true;
            }
            width = 120;
            height = 30;
            top = 0;
            return false;
        }

        public static void SetTitle(string title)
        {
            try
            {
                Console.Title = title;
            }
            catch (Exception)
            {
            }
        }

        public static void Restore()
        {
            if (!inited) return;
            if (inAlt) LeaveAlt();
            Write(Esc + "[0m" + Esc + "[?25h");
            Native.SetConsoleMode(In, origIn);
            Native.SetConsoleMode(Out, origOut);
            if (origTitle != null) SetTitle(origTitle);
            inited = false;
        }
    }
}
