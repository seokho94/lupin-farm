using System;
using System.Runtime.InteropServices;

namespace LupinFarm.Ui
{
    /// <summary>Win32 선언 (콘솔 모드, 입력 레코드, 전역 단축키, 대기).</summary>
    internal static class Native
    {
        public const int STD_INPUT_HANDLE = -10;
        public const int STD_OUTPUT_HANDLE = -11;

        // 입력 모드
        public const uint ENABLE_PROCESSED_INPUT = 0x0001;
        public const uint ENABLE_LINE_INPUT = 0x0002;
        public const uint ENABLE_ECHO_INPUT = 0x0004;
        public const uint ENABLE_WINDOW_INPUT = 0x0008;
        public const uint ENABLE_MOUSE_INPUT = 0x0010;
        public const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
        public const uint ENABLE_EXTENDED_FLAGS = 0x0080;
        public const uint ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200;

        // 출력 모드
        public const uint ENABLE_PROCESSED_OUTPUT = 0x0001;
        public const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;
        public const uint DISABLE_NEWLINE_AUTO_RETURN = 0x0008;

        public const ushort KEY_EVENT = 0x0001;
        public const ushort MOUSE_EVENT = 0x0002;
        public const ushort WINDOW_BUFFER_SIZE_EVENT = 0x0004;

        public const uint FROM_LEFT_1ST_BUTTON_PRESSED = 0x0001;
        public const uint MOUSE_MOVED = 0x0001;
        public const uint DOUBLE_CLICK = 0x0002;
        public const uint MOUSE_WHEELED = 0x0004;

        public const uint RIGHT_ALT_PRESSED = 0x0001;
        public const uint LEFT_ALT_PRESSED = 0x0002;
        public const uint RIGHT_CTRL_PRESSED = 0x0004;
        public const uint LEFT_CTRL_PRESSED = 0x0008;
        public const uint SHIFT_PRESSED = 0x0010;

        public const uint WAIT_OBJECT_0 = 0;

        public const uint WM_QUIT = 0x0012;
        public const uint WM_HOTKEY = 0x0312;
        public const uint PM_NOREMOVE = 0x0000;
        public const uint MOD_NOREPEAT = 0x4000;

        public const uint CTRL_C_EVENT = 0;
        public const uint CTRL_BREAK_EVENT = 1;
        public const uint CTRL_CLOSE_EVENT = 2;
        public const uint CTRL_LOGOFF_EVENT = 5;
        public const uint CTRL_SHUTDOWN_EVENT = 6;

        [StructLayout(LayoutKind.Sequential)]
        public struct COORD
        {
            public short X;
            public short Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SMALL_RECT
        {
            public short Left;
            public short Top;
            public short Right;
            public short Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct CONSOLE_SCREEN_BUFFER_INFO
        {
            public COORD dwSize;
            public COORD dwCursorPosition;
            public ushort wAttributes;
            public SMALL_RECT srWindow;
            public COORD dwMaximumWindowSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEY_EVENT_RECORD
        {
            public int bKeyDown;
            public ushort wRepeatCount;
            public ushort wVirtualKeyCode;
            public ushort wVirtualScanCode;
            public ushort UnicodeChar;
            public uint dwControlKeyState;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSE_EVENT_RECORD
        {
            public COORD dwMousePosition;
            public uint dwButtonState;
            public uint dwControlKeyState;
            public uint dwEventFlags;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct INPUT_RECORD
        {
            [FieldOffset(0)] public ushort EventType;
            [FieldOffset(4)] public KEY_EVENT_RECORD KeyEvent;
            [FieldOffset(4)] public MOUSE_EVENT_RECORD MouseEvent;
            [FieldOffset(4)] public COORD WindowBufferSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        public delegate bool ConsoleCtrlDelegate(uint ctrlType);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetConsoleMode(IntPtr hConsole, out uint mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleMode(IntPtr hConsole, uint mode);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool WriteConsoleW(IntPtr hConsole, string buffer, uint charsToWrite, out uint charsWritten, IntPtr reserved);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool ReadConsoleInputW(IntPtr hConsole, [Out] INPUT_RECORD[] buffer, uint length, out uint read);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetNumberOfConsoleInputEvents(IntPtr hConsole, out uint count);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetConsoleScreenBufferInfo(IntPtr hConsole, out CONSOLE_SCREEN_BUFFER_INFO info);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleOutputCP(uint codePage);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax);

        [DllImport("user32.dll")]
        public static extern bool PeekMessageW(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool PostThreadMessageW(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    }
}
