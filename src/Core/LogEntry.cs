using System.Collections.Generic;

namespace LupinFarm.Core
{
    /// <summary>화면 문구 대신 쓰는 "문구 키 + 값". 화면이 문자열 표로 바꾼다.</summary>
    public sealed class Msg
    {
        public readonly string Key;
        public readonly object[] Args;

        public Msg(string key, params object[] args)
        {
            Key = key;
            Args = args ?? new object[0];
        }
    }

    /// <summary>실제 시간(초). 화면에서 언어에 맞춰 "1h 12m" / "1시간 12분"이 된다.</summary>
    public sealed class Dur
    {
        public readonly double Seconds;

        public Dur(double seconds)
        {
            Seconds = seconds;
        }
    }

    /// <summary>여러 조각을 잇는다. Sep이 null이면 문자열 표의 list.sep(" · ").</summary>
    public sealed class Joined
    {
        public readonly List<object> Items;
        public readonly string Sep;

        public Joined(List<object> items)
        {
            Items = items ?? new List<object>();
        }

        public Joined(List<object> items, string sep)
        {
            Items = items ?? new List<object>();
            Sep = sep;
        }

        /// <summary>구분자 없이 이어 붙인 표 한 줄 (Pad와 함께 쓴다).</summary>
        public static Joined Row(params object[] cells)
        {
            return new Joined(new List<object>(cells), "");
        }
    }

    /// <summary>화면 폭(한글 2칸) 기준으로 오른쪽을 공백으로 채운다. 표의 열 맞춤용 (M2 K11).</summary>
    public sealed class Pad
    {
        public readonly object Inner;
        public readonly int Width;

        public Pad(object inner, int width)
        {
            Inner = inner;
            Width = width;
        }
    }

    public enum LogKind
    {
        Input,
        Ok,
        Error,
        Auto,
        Event,
        Info,
        Welcome,
    }

    public sealed class LogEntry
    {
        public LogKind Kind;
        public Msg Title;
        public string Raw;
        public readonly List<object> Lines = new List<object>();

        public static LogEntry Input(string raw)
        {
            var e = new LogEntry();
            e.Kind = LogKind.Input;
            e.Raw = raw;
            return e;
        }

        public static LogEntry Make(LogKind kind, Msg title, params object[] lines)
        {
            var e = new LogEntry();
            e.Kind = kind;
            e.Title = title;
            if (lines != null) e.Lines.AddRange(lines);
            return e;
        }
    }
}
