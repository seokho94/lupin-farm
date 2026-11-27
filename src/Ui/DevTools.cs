using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using LupinFarm.Data;

namespace LupinFarm.Ui
{
    /// <summary>개발용 옵션 (K9): --frame [en|ko] [cands|prompt|folded|first], --sprites.</summary>
    internal static class DevTools
    {
        public static int Frame(string[] args)
        {
            string lang = args.Length > 1 ? args[1] : "en";
            string mode = args.Length > 2 ? args[2] : "";
            var app = App.Preview(lang, mode);
            var b = new ScreenBuffer();
            b.Resize(Content.MinWidth, Content.MinHeight);
            app.Render(b, DateTime.UtcNow);
            WriteUtf8(b.PlainText());
            return 0;
        }

        public static int SpriteSheet()
        {
            string err;
            if (!Terminal.Init(out err))
            {
                Console.Error.WriteLine(err);
                return 1;
            }
            try
            {
                var sb = new StringBuilder();
                var rows = new List<KeyValuePair<string, string[][]>>();
                rows.Add(new KeyValuePair<string, string[][]>("soil", new[] { Sprites.SoilDry, Sprites.SoilWet, Sprites.Locked }));
                foreach (var c in Content.Crops)
                {
                    var arts = new string[4][];
                    for (int st = 0; st < 4; st++) arts[st] = Sprites.Crop(c.Id, st);
                    rows.Add(new KeyValuePair<string, string[][]>(c.Id + (Sprites.HasArt(c.Id) ? "" : " (fallback)"), arts));
                }
                rows.Add(new KeyValuePair<string, string[][]>("sprinkler Lv1-3", new[] { Sprites.Sprinkler(1), Sprites.Sprinkler(2), Sprites.Sprinkler(3) }));
                rows.Add(new KeyValuePair<string, string[][]>("lupin bloom 1-4", new[] { Sprites.Bloom(0), Sprites.Bloom(1), Sprites.Bloom(2), Sprites.Bloom(3) }));
                foreach (var row in rows)
                {
                    sb.Append(row.Key).Append('\n');
                    for (int r = 0; r < Sprites.Size / 2; r++)
                    {
                        foreach (var art in row.Value)
                        {
                            for (int x = 0; x < Sprites.Size; x++)
                            {
                                int top = Pixel(art, x, r * 2, row.Key == "soil");
                                int bot = Pixel(art, x, r * 2 + 1, row.Key == "soil");
                                sb.Append(Terminal.Esc).Append("[38;2;").Append((top >> 16) & 255).Append(';').Append((top >> 8) & 255).Append(';').Append(top & 255).Append('m');
                                sb.Append(Terminal.Esc).Append("[48;2;").Append((bot >> 16) & 255).Append(';').Append((bot >> 8) & 255).Append(';').Append(bot & 255).Append('m');
                                sb.Append('▀');
                            }
                            sb.Append(Terminal.Esc).Append("[0m  ");
                        }
                        sb.Append('\n');
                    }
                }
                Terminal.Write(sb.ToString());
                return 0;
            }
            finally
            {
                Terminal.Restore();
            }
        }

        static int Pixel(string[] art, int x, int y, bool isBase)
        {
            char ch = art[y][x];
            if (!isBase && ch == '.') ch = Sprites.SoilDry[y][x];
            return Sprites.Color(ch);
        }

        static void WriteUtf8(string s)
        {
            var o = Console.OpenStandardOutput();
            var bytes = new UTF8Encoding(false).GetBytes(s);
            o.Write(bytes, 0, bytes.Length);
            o.Flush();
        }
    }
}
