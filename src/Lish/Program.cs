using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace Lish
{
    internal static class Program
    {
        private static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        public static string Version => typeof(Program).Assembly.GetName().Version.ToString(3);

        [STAThread]
        private static int Main(string[] args)
        {
            // 의존 DLL(SSH.NET 등)이 exe 옆에 없으면 exe 내장 리소스에서 로드한다.
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;
            return Run(args);
        }

        // SSH.NET 타입을 참조하는 코드는 리졸버 등록 이후에 JIT 되도록 분리한다.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(string[] args)
        {
            // 내장 D2Coding 글꼴 등록 (Theme 글꼴이 만들어지기 전에)
            Ui.EmbeddedFonts.Register();

            // 빌드 스크립트용: 앱 아이콘(.ico) 생성
            if (args.Length == 2 && args[0] == "--make-icon")
            {
                using (var fs = File.Create(args[1]))
                    Ui.BrandIcon.WriteIco(fs, 16, 20, 24, 32, 40, 48, 64, 128, 256);
                return 0;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);
            Application.Run(new Ui.MainForm(args));
            return 0;
        }

        private static void ShowError(Exception ex)
        {
            if (ex == null) return;
            MessageBox.Show("예기치 않은 오류가 발생했습니다.\n\n" + ex.Message + "\n\n" + ex.StackTrace, "LISH",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static Assembly ResolveEmbedded(object sender, ResolveEventArgs e)
        {
            var name = new AssemblyName(e.Name).Name;
            lock (Loaded)
            {
                if (Loaded.TryGetValue(name, out var cached))
                    return cached;

                using (var stream = typeof(Program).Assembly.GetManifestResourceStream("lib." + name + ".dll"))
                {
                    if (stream == null)
                        return null;
                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        var asm = Assembly.Load(ms.ToArray());
                        Loaded[name] = asm;
                        return asm;
                    }
                }
            }
        }
    }
}
