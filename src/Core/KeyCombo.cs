using System;
using System.Globalization;

namespace LupinFarm.Core
{
    /// <summary>보스키 조합 "Ctrl+Alt+H". RegisterHotKey 값으로 바꾼다.</summary>
    public sealed class KeyCombo
    {
        public const uint ModAlt = 0x1;
        public const uint ModCtrl = 0x2;
        public const uint ModShift = 0x4;
        public const uint ModWin = 0x8;

        public uint Mods;
        public uint Vk;
        public string Display;

        public static bool TryParse(string text, out KeyCombo combo)
        {
            combo = null;
            if (string.IsNullOrEmpty(text)) return false;
            string[] parts = text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return false;

            uint mods = 0;
            string disp = "";
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string m = parts[i].Trim().ToLowerInvariant();
                if (m == "ctrl" || m == "control") mods |= ModCtrl;
                else if (m == "alt") mods |= ModAlt;
                else if (m == "shift") mods |= ModShift;
                else if (m == "win") mods |= ModWin;
                else return false;
            }
            if ((mods & (ModCtrl | ModAlt | ModWin)) == 0) return false;
            if ((mods & ModCtrl) != 0) disp += "Ctrl+";
            if ((mods & ModAlt) != 0) disp += "Alt+";
            if ((mods & ModShift) != 0) disp += "Shift+";
            if ((mods & ModWin) != 0) disp += "Win+";

            string k = parts[parts.Length - 1].Trim();
            uint vk;
            string kd;
            if (!TryKey(k, out vk, out kd)) return false;

            combo = new KeyCombo();
            combo.Mods = mods;
            combo.Vk = vk;
            combo.Display = disp + kd;
            return true;
        }

        static bool TryKey(string k, out uint vk, out string display)
        {
            vk = 0;
            display = null;
            if (k.Length == 1)
            {
                char c = char.ToUpperInvariant(k[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    vk = c;
                    display = c.ToString();
                    return true;
                }
                return false;
            }
            string low = k.ToLowerInvariant();
            if (low == "space") { vk = 0x20; display = "Space"; return true; }
            if (low == "pause") { vk = 0x13; display = "Pause"; return true; }
            if (low.Length >= 2 && low[0] == 'f')
            {
                int n;
                if (int.TryParse(low.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 1 && n <= 12)
                {
                    vk = (uint)(0x70 + n - 1);
                    display = "F" + n.ToString(CultureInfo.InvariantCulture);
                    return true;
                }
            }
            return false;
        }
    }
}
