using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Lish.Core
{
    /// <summary>저장된 접속 세션 하나.</summary>
    [DataContract]
    internal sealed class HostProfile
    {
        [DataMember(Order = 0)] public string Name { get; set; }
        [DataMember(Order = 1, EmitDefaultValue = false)] public string Group { get; set; }
        [DataMember(Order = 2)] public string Host { get; set; }
        [DataMember(Order = 3)] public int Port { get; set; } = 22;
        [DataMember(Order = 4)] public string User { get; set; }
        [DataMember(Order = 5, EmitDefaultValue = false)] public string KeyFile { get; set; }
        /// <summary>점프 호스트(배스천). 저장된 세션 이름 또는 [user@]host[:port]</summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public string Jump { get; set; }
        /// <summary>HTTP/SOCKS 프록시. 예: http://proxy:8080, socks5://user:pw@proxy:1080</summary>
        [DataMember(Order = 7, EmitDefaultValue = false)] public string Proxy { get; set; }
        [DataMember(Order = 8, EmitDefaultValue = false)] public List<string> LocalForwards { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public List<string> DynamicForwards { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public string Encoding { get; set; }
        [DataMember(Order = 11, EmitDefaultValue = false)] public string Term { get; set; }
        [DataMember(Order = 12)] public int KeepAlive { get; set; } = 30;
        [DataMember(Order = 13)] public int Timeout { get; set; } = 15;
        /// <summary>
        /// 예전 버전의 "비밀번호 저장"(DPAPI 암호문). 보안상 기능을 없앴고, 기존 파일에서 읽기만 해서
        /// ProfileStore.Load 가 지우고 다시 저장한다. 앱 코드에서는 쓰지 않는다.
        /// </summary>
        [DataMember(Name = "SavedPassword", Order = 14, EmitDefaultValue = false)] private string LegacySavedPassword { get; set; }
        [DataMember(Order = 15, EmitDefaultValue = false)] public string Note { get; set; }

        public List<string> LocalForwardList => LocalForwards ?? (LocalForwards = new List<string>());
        public List<string> DynamicForwardList => DynamicForwards ?? (DynamicForwards = new List<string>());
        public string TermOrDefault => string.IsNullOrEmpty(Term) ? "xterm-256color" : Term;
        public string Title => string.IsNullOrEmpty(Name) ? Target : Name;
        public string Target => (string.IsNullOrEmpty(User) ? "" : User + "@") + Host + (Port == 22 ? "" : ":" + Port);

        public Encoding GetEncoding() => ResolveEncoding(Encoding);

        public static Encoding ResolveEncoding(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new UTF8Encoding(false);
            var n = name.Trim().ToLowerInvariant();
            if (n == "utf8" || n == "utf-8")
                return new UTF8Encoding(false);
            if (n == "cp949" || n == "ms949" || n == "uhc")
                return System.Text.Encoding.GetEncoding(949);
            return System.Text.Encoding.GetEncoding(n);
        }

        public string Display()
        {
            var target = Target;
            if (!string.IsNullOrEmpty(Jump))
                target += "  (via " + Jump + ")";
            return target;
        }

        public HostProfile Clone()
        {
            var c = (HostProfile)MemberwiseClone();
            c.LocalForwards = LocalForwards == null ? null : new List<string>(LocalForwards);
            c.DynamicForwards = DynamicForwards == null ? null : new List<string>(DynamicForwards);
            c.LegacySavedPassword = null;
            return c;
        }

        /// <summary>예전 버전이 저장한 비밀번호가 남아 있으면 지운다. 지웠으면 true.</summary>
        public bool ClearLegacyPassword()
        {
            if (LegacySavedPassword == null)
                return false;
            LegacySavedPassword = null;
            return true;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext _)
        {
            // DataContract 역직렬화는 생성자/초기화식을 건너뛰므로 기본값을 여기서 채운다.
            Port = 22;
            KeepAlive = 30;
            Timeout = 15;
        }
    }

    /// <summary>
    /// 데이터 폴더 위치.
    /// 1) 환경변수 LISH_HOME  2) exe 옆 lish-data 폴더(포터블 모드)  3) %APPDATA%\Lish
    /// </summary>
    internal static class Paths
    {
        private static string _dataDir;

        public static string DataDir
        {
            get
            {
                if (_dataDir != null)
                    return _dataDir;
                var env = Environment.GetEnvironmentVariable("LISH_HOME");
                var portable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lish-data");
                if (!string.IsNullOrEmpty(env))
                    _dataDir = env;
                else if (Directory.Exists(portable))
                    _dataDir = portable;
                else
                    _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lish");
                Directory.CreateDirectory(_dataDir);
                return _dataDir;
            }
        }

        public static string HostsFile => Path.Combine(DataDir, "hosts.json");
        public static string FoldersFile => Path.Combine(DataDir, "folders.json");
        public static string SettingsFile => Path.Combine(DataDir, "settings.json");
        public static string KnownHostsFile => Path.Combine(DataDir, "known_hosts");
        public static string UserSshDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
    }

    internal static class JsonFile
    {
        public static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path))
                return null;
            var bytes = File.ReadAllBytes(path);
            // 메모장 등이 붙인 UTF-8 BOM 제거
            var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            if (bytes.Length - start == 0)
                return null;
            using (var ms = new MemoryStream(bytes, start, bytes.Length - start))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(ms);
        }

        public static void Write<T>(string path, T value)
        {
            var tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(fs, new UTF8Encoding(false), false, true, "  "))
                new DataContractJsonSerializer(typeof(T)).WriteObject(writer, value);
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
    }

    /// <summary>
    /// 세션 폴더 경로. HostProfile.Group 에 "운영/웹" 처럼 '/' 로 구분해 저장한다 (예전 단일 그룹 값도 그대로 최상위 폴더).
    /// </summary>
    internal static class FolderPath
    {
        public const char Separator = '/';

        public static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var parts = path.Split(Separator).Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            return parts.Length == 0 ? null : string.Join(Separator.ToString(), parts);
        }

        public static string Combine(string parent, string name) =>
            string.IsNullOrEmpty(parent) ? name : parent + Separator + name;

        public static string NameOf(string path)
        {
            var i = path.LastIndexOf(Separator);
            return i < 0 ? path : path.Substring(i + 1);
        }

        public static string ParentOf(string path)
        {
            var i = path?.LastIndexOf(Separator) ?? -1;
            return i < 0 ? null : path.Substring(0, i);
        }

        /// <summary>path 가 folder 자신이거나 그 하위인지.</summary>
        public static bool IsUnder(string path, string folder) =>
            !string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(folder)
            && (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(folder + Separator, StringComparison.OrdinalIgnoreCase));

        /// <summary>folder(또는 그 하위)에 있던 path 를 newFolder 아래로 옮긴 경로.</summary>
        public static string Rebase(string path, string folder, string newFolder) =>
            newFolder + path.Substring(folder.Length);

        public static string Display(string path) => path?.Replace(Separator.ToString(), " / ");

        /// <summary>폴더 이름으로 쓸 수 없으면 그 이유.</summary>
        public static string Validate(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "폴더 이름을 입력하세요.";
            if (name.IndexOf(Separator) >= 0)
                return "폴더 이름에는 '/' 를 쓸 수 없습니다.";
            return null;
        }
    }

    /// <summary>폴더 경로를 단계별 이름순으로 비교 ("운영/웹" 이 "운영 2" 보다 앞에, "운영" 바로 뒤에).</summary>
    internal sealed class FolderComparer : IComparer<string>
    {
        public static readonly FolderComparer Instance = new FolderComparer();

        public int Compare(string x, string y)
        {
            var a = (x ?? "").Split(FolderPath.Separator);
            var b = (y ?? "").Split(FolderPath.Separator);
            for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                var c = StringComparer.CurrentCultureIgnoreCase.Compare(a[i], b[i]);
                if (c != 0)
                    return c;
            }
            return a.Length.CompareTo(b.Length);
        }
    }

    internal sealed class ProfileStore
    {
        private readonly List<HostProfile> _profiles;
        /// <summary>빈 폴더도 남도록 따로 저장하는 폴더 목록 (세션의 Group 경로와 그 상위는 자동 포함).</summary>
        private readonly HashSet<string> _folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private ProfileStore(List<HostProfile> profiles) => _profiles = profiles;

        public IReadOnlyList<HostProfile> All => _profiles;

        public static ProfileStore Empty() => new ProfileStore(new List<HostProfile>());

        /// <summary>마지막 Load 에서 파일이 손상되어 있었다면 그 안내 문구.</summary>
        public static string LoadError { get; private set; }

        public static ProfileStore Load()
        {
            List<HostProfile> list;
            try
            {
                list = JsonFile.Read<List<HostProfile>>(Paths.HostsFile) ?? new List<HostProfile>();
            }
            catch (SerializationException ex)
            {
                // 손상된 파일은 백업해 두고 빈 목록으로 시작한다.
                var backup = Paths.HostsFile + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                File.Copy(Paths.HostsFile, backup, true);
                LoadError = $"세션 파일을 읽을 수 없어 백업했습니다.\n{backup}\n\n{ex.Message}";
                list = new List<HostProfile>();
            }
            // 예전 버전의 "비밀번호 저장" 값은 디스크에서 지운다 (이름 없는 항목도 버려지기 전에 확인)
            var hadLegacyPassword = false;
            foreach (var p in list.Where(p => p != null))
                hadLegacyPassword |= p.ClearLegacyPassword();
            var store = new ProfileStore(list.Where(p => p != null && !string.IsNullOrEmpty(p.Name)).ToList());
            foreach (var p in store._profiles)
            {
                p.Group = FolderPath.Normalize(p.Group);
                store.AddFolderPath(p.Group);   // 마지막 세션을 지워도 폴더는 남게
            }
            try
            {
                foreach (var f in JsonFile.Read<List<string>>(Paths.FoldersFile) ?? new List<string>())
                    store.AddFolderPath(FolderPath.Normalize(f));
            }
            catch (SerializationException)
            {
                // 폴더 목록이 깨져도 세션의 폴더는 남으므로 빈 폴더만 잃는다.
            }
            store.Sort();
            if (hadLegacyPassword)
            {
                try
                {
                    store.Save();
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // 지금 못 지우면 다음 실행(또는 다음 저장)에서 다시 지운다.
                }
            }
            return store;
        }

        public void Save()
        {
            JsonFile.Write(Paths.HostsFile, _profiles);
            JsonFile.Write(Paths.FoldersFile, Folders.ToList());
        }

        public HostProfile Find(string name) =>
            _profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>모든 폴더 경로 (상위 폴더 포함, 정렬됨).</summary>
        public IEnumerable<string> Folders
        {
            get
            {
                var all = new HashSet<string>(_folders, StringComparer.OrdinalIgnoreCase);
                foreach (var g in _profiles.Select(p => p.Group).Where(g => !string.IsNullOrEmpty(g)))
                    for (var f = g; f != null; f = FolderPath.ParentOf(f))
                        all.Add(f);
                return all.OrderBy(f => f, FolderComparer.Instance);
            }
        }

        public bool FolderExists(string path) =>
            !string.IsNullOrEmpty(path) && Folders.Contains(path, StringComparer.OrdinalIgnoreCase);

        /// <summary>folder(하위 포함)에 든 세션. folder 가 null 이면 전체.</summary>
        public IEnumerable<HostProfile> SessionsUnder(string folder) =>
            folder == null ? _profiles : _profiles.Where(p => FolderPath.IsUnder(p.Group, folder));

        /// <summary>parent 아래에 새 폴더를 만들고 그 경로를 돌려준다. 이름이 잘못됐거나 이미 있으면 예외.</summary>
        public string CreateFolder(string parent, string name)
        {
            name = name?.Trim();
            var error = FolderPath.Validate(name);
            if (error != null)
                throw new ArgumentException(error);
            var path = FolderPath.Combine(parent, name);
            if (FolderExists(path))
                throw new ArgumentException($"'{name}' 폴더가 이미 있습니다.");
            AddFolderPath(path);
            return path;
        }

        /// <summary>폴더 이름만 바꾼다 (하위 폴더·세션도 따라감). 새 경로를 돌려준다.</summary>
        public string RenameFolder(string path, string newName)
        {
            newName = newName?.Trim();
            var error = FolderPath.Validate(newName);
            if (error != null)
                throw new ArgumentException(error);
            var target = FolderPath.Combine(FolderPath.ParentOf(path), newName);
            if (!string.Equals(target, path, StringComparison.OrdinalIgnoreCase) && FolderExists(target))
                throw new ArgumentException($"'{newName}' 폴더가 이미 있습니다.");
            Relocate(path, target);
            return target;
        }

        /// <summary>폴더를 newParent(null = 최상위) 아래로 옮긴다. 새 경로를 돌려준다.</summary>
        public string MoveFolder(string path, string newParent)
        {
            if (FolderPath.IsUnder(newParent, path))
                throw new ArgumentException("폴더를 자기 자신이나 하위 폴더로 옮길 수 없습니다.");
            var target = FolderPath.Combine(newParent, FolderPath.NameOf(path));
            if (string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
                return path;
            if (FolderExists(target))
                throw new ArgumentException($"'{FolderPath.Display(newParent) ?? "최상위"}' 에 '{FolderPath.NameOf(path)}' 폴더가 이미 있습니다.");
            Relocate(path, target);
            return target;
        }

        /// <summary>폴더와 그 안의 하위 폴더·세션을 모두 지운다.</summary>
        public void DeleteFolder(string path)
        {
            _profiles.RemoveAll(p => FolderPath.IsUnder(p.Group, path));
            _folders.RemoveWhere(f => FolderPath.IsUnder(f, path));
        }

        private void Relocate(string path, string target)
        {
            foreach (var p in _profiles.Where(p => FolderPath.IsUnder(p.Group, path)))
                p.Group = FolderPath.Rebase(p.Group, path, target);
            var moved = _folders.Where(f => FolderPath.IsUnder(f, path)).ToList();
            _folders.RemoveWhere(f => FolderPath.IsUnder(f, path));
            foreach (var f in moved)
                _folders.Add(FolderPath.Rebase(f, path, target));
            _folders.Add(target);
        }

        private void AddFolderPath(string path)
        {
            for (var f = path; f != null; f = FolderPath.ParentOf(f))
                _folders.Add(f);
        }

        public void Upsert(HostProfile profile)
        {
            Remove(profile.Name);
            profile.Group = FolderPath.Normalize(profile.Group);
            if (profile.Group != null)
                AddFolderPath(profile.Group);
            _profiles.Add(profile);
            Sort();
        }

        public bool Remove(string name) =>
            _profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;

        public string UniqueName(string baseName)
        {
            if (Find(baseName) == null)
                return baseName;
            for (var i = 2; ; i++)
            {
                var n = $"{baseName} ({i})";
                if (Find(n) == null)
                    return n;
            }
        }

        private void Sort() => _profiles.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));
    }

    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember] public string FontName { get; set; }
        [DataMember] public float FontSize { get; set; } = 10.5f;
        [DataMember] public int Scrollback { get; set; } = 5000;
        [DataMember] public bool CopyOnSelect { get; set; } = true;
        [DataMember] public bool RightClickPaste { get; set; }
        [DataMember] public bool ConfirmMultilinePaste { get; set; } = true;
        [DataMember] public int WindowX { get; set; } = -1;
        [DataMember] public int WindowY { get; set; } = -1;
        [DataMember] public int WindowWidth { get; set; } = 1280;
        [DataMember] public int WindowHeight { get; set; } = 800;
        [DataMember] public bool WindowMaximized { get; set; }
        [DataMember] public int SidebarWidth { get; set; } = 250;
        /// <summary>사이드바 SFTP 탭일 때의 폭 (0 이면 자동: 세션 탭 폭과 최소 폭 중 큰 값)</summary>
        [DataMember] public int SftpSidebarWidth { get; set; }
        /// <summary>SFTP 탭 "터미널 폴더 따라가기"</summary>
        [DataMember] public bool SftpFollowTerminal { get; set; } = true;

        [OnDeserializing]
        private void OnDeserializing(StreamingContext _)
        {
            FontSize = 10.5f;
            Scrollback = 5000;
            CopyOnSelect = true;
            ConfirmMultilinePaste = true;
            WindowX = WindowY = -1;
            WindowWidth = 1280;
            WindowHeight = 800;
            SidebarWidth = 250;
            SftpFollowTerminal = true;
        }

        public static AppSettings Load()
        {
            try
            {
                return JsonFile.Read<AppSettings>(Paths.SettingsFile) ?? new AppSettings();
            }
            catch (SerializationException)
            {
                return new AppSettings();
            }
        }

        public void Save()
        {
            try { JsonFile.Write(Paths.SettingsFile, this); } catch (IOException) { /* 설정 저장 실패는 무시 */ }
        }
    }
}
