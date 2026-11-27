using System.Collections.Generic;

namespace LupinFarm.Ui
{
    internal enum InputKind
    {
        Key,
        Mouse,
        Resize,
    }

    internal struct InputEvent
    {
        public InputKind Kind;
        public ushort Vk;
        public char Ch;
        public bool Ctrl;
        public bool Alt;
        public bool Shift;
        public int X;
        public int Y;
        public bool Click;
        public int Wheel;
    }

    /// <summary>ReadConsoleInput → 키·마우스·크기 이벤트 (D8).</summary>
    internal static class InputReader
    {
        static readonly Native.INPUT_RECORD[] buffer = new Native.INPUT_RECORD[128];
        static bool leftDown;

        public static List<InputEvent> Poll(int timeoutMs)
        {
            var list = new List<InputEvent>();
            if (Native.WaitForSingleObject(Terminal.In, (uint)timeoutMs) != Native.WAIT_OBJECT_0) return list;
            uint count;
            if (!Native.GetNumberOfConsoleInputEvents(Terminal.In, out count) || count == 0) return list;
            uint read;
            if (!Native.ReadConsoleInputW(Terminal.In, buffer, (uint)buffer.Length, out read)) return list;

            for (int i = 0; i < read; i++)
            {
                var rec = buffer[i];
                if (rec.EventType == Native.KEY_EVENT)
                {
                    var k = rec.KeyEvent;
                    if (k.bKeyDown == 0) continue;
                    var e = new InputEvent();
                    e.Kind = InputKind.Key;
                    e.Vk = k.wVirtualKeyCode;
                    e.Ch = (char)k.UnicodeChar;
                    e.Ctrl = (k.dwControlKeyState & (Native.LEFT_CTRL_PRESSED | Native.RIGHT_CTRL_PRESSED)) != 0;
                    e.Alt = (k.dwControlKeyState & (Native.LEFT_ALT_PRESSED | Native.RIGHT_ALT_PRESSED)) != 0;
                    e.Shift = (k.dwControlKeyState & Native.SHIFT_PRESSED) != 0;
                    int repeat = k.wRepeatCount < 1 ? 1 : k.wRepeatCount;
                    for (int r = 0; r < repeat; r++) list.Add(e);
                }
                else if (rec.EventType == Native.MOUSE_EVENT)
                {
                    var m = rec.MouseEvent;
                    var e = new InputEvent();
                    e.Kind = InputKind.Mouse;
                    e.X = m.dwMousePosition.X;
                    e.Y = m.dwMousePosition.Y;
                    if ((m.dwEventFlags & Native.MOUSE_WHEELED) != 0)
                    {
                        short delta = (short)(m.dwButtonState >> 16);
                        e.Wheel = delta > 0 ? 1 : -1;
                        list.Add(e);
                        continue;
                    }
                    bool down = (m.dwButtonState & Native.FROM_LEFT_1ST_BUTTON_PRESSED) != 0;
                    if (down && !leftDown && (m.dwEventFlags & Native.MOUSE_MOVED) == 0)
                    {
                        e.Click = true;
                        list.Add(e);
                    }
                    leftDown = down;
                }
                else if (rec.EventType == Native.WINDOW_BUFFER_SIZE_EVENT)
                {
                    var e = new InputEvent();
                    e.Kind = InputKind.Resize;
                    list.Add(e);
                }
            }
            return list;
        }
    }
}
