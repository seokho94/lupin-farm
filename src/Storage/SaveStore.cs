using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using LupinFarm.Core;

namespace LupinFarm.Storage
{
    /// <summary>내보내기 파일 하나: 현재 판 + 프로필 (M5 K8).</summary>
    public sealed class ExportFile
    {
        public const string FormatName = "lupin-farm-export";
        public const int CurrentVersion = 1;

        public string Format { get; set; }
        public int Version { get; set; }

        /// <summary>내보낸 시각 (UTC, 왕복 형식).</summary>
        public string ExportedAt { get; set; }
        public GameState Save { get; set; }
        public Profile Profile { get; set; }
    }

    /// <summary>내보내기 파일을 읽지 못한 이유.</summary>
    public enum ExportError
    {
        None,
        Missing,
        Broken,
        Format,
        Version,
    }

    /// <summary>
    /// save.json(현재 판)와 profile.json(판을 넘어 남는 것) 읽기·쓰기.
    /// 임시 파일에 쓴 뒤 바꿔치기하고 직전 버전을 .bak으로 남긴다 (FR-SET-07).
    /// </summary>
    public sealed class SaveStore
    {
        public readonly string Dir;
        readonly string savePath;
        readonly string profilePath;
        readonly string errorPath;

        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public SaveStore(string dir)
        {
            Dir = dir;
            savePath = Path.Combine(dir, "save.json");
            profilePath = Path.Combine(dir, "profile.json");
            errorPath = Path.Combine(dir, "error.log");
        }

        public static string DefaultDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LupinFarm");
        }

        public bool HasSave
        {
            get { return File.Exists(savePath) || File.Exists(savePath + ".bak"); }
        }

        /// <summary>세이브를 읽는다. 실패하면 .bak, 그래도 실패하면 null (FR-SET-08).</summary>
        public GameState LoadSave(out bool fromBackup)
        {
            fromBackup = false;
            var s = Read<GameState>(savePath);
            if (s == null)
            {
                s = Read<GameState>(savePath + ".bak");
                fromBackup = s != null;
            }
            if (s != null) s.Normalize();
            return s;
        }

        public Profile LoadProfile()
        {
            var p = Read<Profile>(profilePath) ?? Read<Profile>(profilePath + ".bak");
            if (p != null) p.Normalize();
            return p;
        }

        public void Save(GameState s)
        {
            Write(savePath, s);
        }

        public void SaveProfile(Profile p)
        {
            Write(profilePath, p);
        }

        public void DeleteAll()
        {
            foreach (var path in new[] { savePath, savePath + ".bak", profilePath, profilePath + ".bak" })
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (IOException)
                {
                }
            }
        }

        public void LogError(Exception e)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.AppendAllText(errorPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + e + Environment.NewLine,
                    Utf8);
            }
            catch (Exception)
            {
            }
        }

        public string ErrorLogPath
        {
            get { return errorPath; }
        }

        // ------------------------------------------------------------------ 내보내기·가져오기 (M5 K6~K8)

        /// <summary>내보낸 파일의 기본 폴더: 저장 폴더의 export.</summary>
        public string ExportDir
        {
            get { return Path.Combine(Dir, "export"); }
        }

        /// <summary>"lupin-20261127-161002.json" 같은 이름.</summary>
        public static string StampName(string prefix, DateTime local)
        {
            return prefix + "-" + local.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json";
        }

        static string Unquote(string arg)
        {
            string a = (arg ?? "").Trim();
            if (a.Length >= 2 && a[0] == '"' && a[a.Length - 1] == '"') a = a.Substring(1, a.Length - 2).Trim();
            return a;
        }

        /// <summary>
        /// 내보낼 경로. 비었으면 export 폴더에 기본 이름, 폴더면 그 안에 기본 이름, 확장자가 없으면 .json.
        /// 상대 경로는 export 폴더 기준이다 (현재 폴더에 쓰지 않는다, K6).
        /// </summary>
        public string ExportPath(string arg, DateTime local)
        {
            string a = Unquote(arg);
            string name = StampName("lupin", local);
            if (a.Length == 0) return Path.Combine(ExportDir, name);
            string full = Path.IsPathRooted(a) ? a : Path.Combine(ExportDir, a);
            if (Directory.Exists(full) || a.EndsWith("\\") || a.EndsWith("/")) return Path.Combine(full, name);
            if (Path.GetExtension(full).Length == 0) full += ".json";
            return Path.GetFullPath(full);
        }

        /// <summary>가져올 경로. 상대 경로는 export 폴더 기준, 확장자가 없으면 .json도 찾아본다.</summary>
        public string ImportPath(string arg)
        {
            string a = Unquote(arg);
            string full = Path.IsPathRooted(a) ? a : Path.Combine(ExportDir, a);
            if (!File.Exists(full) && Path.GetExtension(full).Length == 0 && File.Exists(full + ".json")) full += ".json";
            return Path.GetFullPath(full);
        }

        /// <summary>판과 프로필을 파일 하나로 쓴다. 이미 있던 파일을 바꿨으면 true.</summary>
        public bool Export(string path, GameState s, Profile p, DateTime utcNow)
        {
            var f = new ExportFile();
            f.Format = ExportFile.FormatName;
            f.Version = ExportFile.CurrentVersion;
            f.ExportedAt = utcNow.ToString("o", CultureInfo.InvariantCulture);
            f.Save = s;
            f.Profile = p;
            bool existed = File.Exists(path);
            Write(path, f);
            return existed;
        }

        /// <summary>내보낸 파일을 읽는다. 형식·버전을 확인하고 판·프로필을 Normalize한다. 실패하면 null과 이유.</summary>
        public static ExportFile ReadExport(string path, out ExportError error)
        {
            error = ExportError.None;
            if (!File.Exists(path))
            {
                error = ExportError.Missing;
                return null;
            }
            ExportFile f;
            try
            {
                var js = new JavaScriptSerializer();
                js.MaxJsonLength = int.MaxValue;
                f = js.Deserialize<ExportFile>(File.ReadAllText(path, Utf8));
            }
            catch (Exception)
            {
                error = ExportError.Broken;
                return null;
            }
            if (f == null || f.Format != ExportFile.FormatName)
            {
                error = ExportError.Format;
                return null;
            }
            if (f.Version > ExportFile.CurrentVersion)
            {
                error = ExportError.Version;
                return f;
            }
            if (f.Save == null || f.Profile == null)
            {
                error = ExportError.Broken;
                return null;
            }
            f.Save.Normalize();
            f.Profile.Normalize();
            return f;
        }

        /// <summary>export 폴더의 .json 파일, 최근 것부터.</summary>
        public List<FileInfo> ListExports()
        {
            var list = new List<FileInfo>();
            try
            {
                var d = new DirectoryInfo(ExportDir);
                if (!d.Exists) return list;
                list.AddRange(d.GetFiles("*.json"));
            }
            catch (Exception)
            {
                return list;
            }
            list.Sort(delegate(FileInfo x, FileInfo y) { return y.LastWriteTimeUtc.CompareTo(x.LastWriteTimeUtc); });
            return list;
        }

        static T Read<T>(string path) where T : class
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path, Utf8);
                return new JavaScriptSerializer().Deserialize<T>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void Write(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = Pretty(new JavaScriptSerializer().Serialize(value));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json, Utf8);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, path + ".bak", true);
                    return;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
                File.Copy(path, path + ".bak", true);
                File.Delete(path);
            }
            File.Move(tmp, path);
        }

        /// <summary>사람이 읽을 수 있게 들여쓴다.</summary>
        public static string Pretty(string json)
        {
            var sb = new StringBuilder(json.Length * 2);
            int indent = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (inString)
                {
                    sb.Append(ch);
                    if (ch == '\\' && i + 1 < json.Length)
                    {
                        sb.Append(json[++i]);
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }
                    continue;
                }
                switch (ch)
                {
                    case '"':
                        inString = true;
                        sb.Append(ch);
                        break;
                    case '{':
                    case '[':
                        char close = ch == '{' ? '}' : ']';
                        if (i + 1 < json.Length && json[i + 1] == close)
                        {
                            sb.Append(ch).Append(close);
                            i++;
                            break;
                        }
                        sb.Append(ch);
                        indent++;
                        NewLine(sb, indent);
                        break;
                    case '}':
                    case ']':
                        indent--;
                        NewLine(sb, indent);
                        sb.Append(ch);
                        break;
                    case ',':
                        sb.Append(ch);
                        NewLine(sb, indent);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }
            sb.Append('\n');
            return sb.ToString();
        }

        static void NewLine(StringBuilder sb, int indent)
        {
            sb.Append('\n');
            sb.Append(' ', Math.Max(0, indent) * 2);
        }
    }
}
