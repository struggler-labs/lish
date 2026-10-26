using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;

namespace Lish.Ui
{
    /// <summary>
    /// exe 에 내장한 글꼴(D2Coding, SIL OFL 1.1 — assets\fonts\OFL.txt)을 이 프로세스에서만 쓰도록 등록한다.
    /// 터미널(GDI ExtTextOut)은 AddFontMemResourceEx 로, WinForms Font(GDI+)는 PrivateFontCollection 으로 찾는다.
    /// PC에 같은 글꼴이 설치돼 있으면 설치본을 쓰고 등록하지 않는다.
    /// </summary>
    internal static class EmbeddedFonts
    {
        private const string Prefix = "font.";

        private static readonly PrivateFontCollection Collection = new PrivateFontCollection();
        // GDI+ 가 프로세스 종료까지 이 메모리를 참조하므로 해제하지 않는다.
        private static readonly List<IntPtr> Memory = new List<IntPtr>();

        /// <summary>시작 시 Theme 보다 먼저 한 번 호출한다.</summary>
        public static void Register()
        {
            var asm = typeof(EmbeddedFonts).Assembly;
            using (var installed = new InstalledFontCollection())
            {
                foreach (var res in asm.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)))
                {
                    // 리소스 이름: font.<글꼴 이름>.<스타일>.ttf.gz
                    var family = res.Substring(Prefix.Length).Split('.')[0];
                    if (installed.Families.Any(f => string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    try
                    {
                        Add(asm.GetManifestResourceStream(res));
                    }
                    catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ExternalException)
                    {
                        // 내장 글꼴을 못 쓰면 설치된 다른 고정폭 글꼴로 대체된다.
                    }
                }
            }
        }

        private static void Add(Stream resource)
        {
            byte[] data;
            using (resource)
            using (var gz = new GZipStream(resource, CompressionMode.Decompress))
            using (var ms = new MemoryStream())
            {
                gz.CopyTo(ms);
                data = ms.ToArray();
            }
            var ptr = Marshal.AllocCoTaskMem(data.Length);
            Marshal.Copy(data, 0, ptr, data.Length);
            Memory.Add(ptr);
            uint count = 0;
            AddFontMemResourceEx(ptr, (uint)data.Length, IntPtr.Zero, ref count);
            Collection.AddMemoryFont(ptr, data.Length);
        }

        /// <summary>내장 글꼴 가족. 없으면 null.</summary>
        public static FontFamily Find(string name) =>
            Collection.Families.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>내장 글꼴이면 그 가족으로, 아니면 설치된 글꼴 이름으로 만든다.</summary>
        public static Font Create(string name, float size, FontStyle style = FontStyle.Regular, GraphicsUnit unit = GraphicsUnit.Point)
        {
            var family = Find(name);
            return family != null ? new Font(family, size, style, unit) : new Font(name, size, style, unit);
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);
    }
}
