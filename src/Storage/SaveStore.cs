using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using LupinFarm.Core;

namespace LupinFarm.Storage
{
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
