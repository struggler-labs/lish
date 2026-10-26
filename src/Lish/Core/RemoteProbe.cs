using System;
using System.Globalization;
using Renci.SshNet;

namespace Lish.Core
{
    /// <summary>원격 서버 자원 사용량 (하단 상태바 모니터용). 모르는 값은 null.</summary>
    internal sealed class RemoteStats
    {
        public double? CpuPercent;
        public long? MemTotal, MemUsed;     // bytes
        public long? DiskTotal, DiskUsed;   // bytes
        public string DiskMount;
    }

    /// <summary>
    /// 터미널과 같은 SSH 연결에 exec 채널을 열어 셸의 현재 폴더와 서버 자원 사용량을 조회한다.
    /// 터미널에는 아무것도 입력하지 않는다.
    /// </summary>
    /// <remarks>
    /// 원리 (현재 폴더): OpenSSH/Dropbear 는 연결 하나의 모든 채널을 같은 연결별 프로세스
    /// ("sshd: user@pts/0", "sshd-session" 등)가 fork 해서 처리한다. 그래서 exec 로 띄운 스크립트에서
    /// 부모를 따라 올라가 그 sshd 프로세스를 찾고, 그 자식 중 제어 터미널(pty)이 있는 프로세스
    /// = 이 연결의 대화형 로그인 셸을 찾아 /proc/PID/cwd 를 읽는다.
    /// 로그인 셸이 bash/zsh 이면 마지막 명령을 exec 로 바꿔치기해 $PPID 가 바로 sshd 가 되고,
    /// csh/fish 등은 사이에 로그인 셸이 한 단계 끼므로 부모를 최대 4단계까지 올라간다.
    ///
    /// 원격 스크립트는 POSIX sh 로 작성하고 <c>/bin/sh -c '한 줄'</c> 로 감싼다.
    /// sshd 는 사용자의 로그인 셸로 명령을 실행하므로 csh/fish 에서도 깨지지 않도록
    /// 스크립트에는 작은따옴표·느낌표·백슬래시·줄바꿈을 쓰지 않는다 (csh 는 따옴표 안 줄바꿈 불가).
    /// 셸 초기화 파일(.cshrc, .zshenv 등)이 뭔가 출력해도 무시되도록 결과 줄에는 '@' 접두어를 붙인다.
    /// </remarks>
    internal sealed class RemoteProbe
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        // 셸 현재 폴더 조회 스크립트.
        // c = 제외할 자기 자신 계열 PID 목록, s = 연결별 sshd PID.
        // stat 의 ')' 뒤 필드: $1 상태, $2 PPID, $3 pgrp, $4 session, $5 tty_nr (제어 터미널, 0 = 없음).
        // fd 0 대신 tty_nr 로 판정한다 — tcsh 대화형 셸은 fd 0 이 pts 가 아니어서 fd 0 검사로는 못 찾았다.
        private const string CwdScript =
            "c=\" $$ \"; a=$PPID; s=; i=0; " +
            "while [ $i -lt 4 ] && [ -n \"$a\" ] && [ \"$a\" -gt 1 ]; do " +
              "n=; read -r n < /proc/$a/comm; " +
              "case \"$n\" in sshd*|dropbear*) s=$a; break;; esac; " +
              "c=\"$c$a \"; l=; read -r l < /proc/$a/stat || break; l=${l##*) }; set -- $l; a=$2; i=$((i+1)); " +
            "done; " +
            "[ -n \"$s\" ] || { echo @NOSSHD; exit 0; }; " +
            "for d in /proc/[0-9]*; do " +
              "p=${d#/proc/}; " +
              "case \"$c\" in *\" $p \"*) continue;; esac; " +
              "l=; read -r l < $d/stat 2>/dev/null || continue; l=${l##*) }; set -- $l; " +
              "[ \"$2\" = \"$s\" ] && [ \"$5\" != 0 ] || continue; " +
              "echo \"@P $p $(readlink $d/cwd 2>/dev/null)\"; " +
            "done; echo @END";

        // 자원 사용량 스크립트. {0} = CPU 두 번째 샘플 전 대기 (첫 호출만).
        private const string StatsScript =
            "read -r l < /proc/stat && echo \"@C1 $l\"; " +
            "{0}" +
            "while read -r k v u; do case \"$k\" in MemTotal:|MemAvailable:|MemFree:|Buffers:|Cached:|SReclaimable:) echo \"@M $k $v\";; esac; done < /proc/meminfo; " +
            "d=$HOME; [ -n \"$d\" ] && [ -d \"$d\" ] || d=/; " +
            "df -Pk \"$d\" 2>/dev/null | while read -r x; do echo \"@D $x\"; done; echo @END";

        private const string SecondCpuSample =
            "sleep 0.3 2>/dev/null || sleep 1; read -r l < /proc/stat && echo \"@C2 $l\"; ";

        private readonly SshClient _client;
        private readonly object _cpuLock = new object();
        private ulong[] _prevCpu;   // 직전 /proc/stat cpu 줄 (전체, 유휴)

        public RemoteProbe(SshClient client)
        {
            _client = client;
        }

        /// <summary>터미널 셸의 현재 작업 폴더. 알 수 없으면 null.</summary>
        public string QueryShellCwd()
        {
            string output = Run(CwdScript);
            if (output == null || output.IndexOf("@END", StringComparison.Ordinal) < 0) return null;

            // 같은 연결에 터미널이 여럿이면 가장 먼저 열린(PID 가 작은) 셸을 택한다.
            long bestPid = long.MaxValue;
            string best = null;
            foreach (string raw in output.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (!line.StartsWith("@P ", StringComparison.Ordinal)) continue;
                string rest = line.Substring(3);
                int sp = rest.IndexOf(' ');
                if (sp <= 0) continue;
                if (!long.TryParse(rest.Substring(0, sp), NumberStyles.None, CultureInfo.InvariantCulture, out long pid)) continue;
                string cwd = rest.Substring(sp + 1);
                if (cwd.Length == 0 || cwd[0] != '/') continue;         // 권한 없음 등
                if (cwd.EndsWith(" (deleted)", StringComparison.Ordinal)) continue; // 지워진 폴더
                if (pid < bestPid) { bestPid = pid; best = cwd; }
            }
            return best;
        }

        /// <summary>CPU/RAM/디스크 사용량. 조회 실패 시 null.</summary>
        public RemoteStats QueryStats()
        {
            bool first;
            lock (_cpuLock) first = _prevCpu == null;

            string output = Run(string.Format(CultureInfo.InvariantCulture, StatsScript, first ? SecondCpuSample : ""));
            if (output == null || output.IndexOf("@END", StringComparison.Ordinal) < 0) return null;

            var st = new RemoteStats();
            ulong[] c1 = null, c2 = null;
            long? memTotal = null, memAvail = null, memFree = null, buffers = null, cached = null, sreclaim = null;
            string dfLine = null;

            foreach (string raw in output.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("@C1 ", StringComparison.Ordinal)) c1 = ParseCpu(line.Substring(4));
                else if (line.StartsWith("@C2 ", StringComparison.Ordinal)) c2 = ParseCpu(line.Substring(4));
                else if (line.StartsWith("@M ", StringComparison.Ordinal))
                {
                    string[] f = line.Substring(3).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (f.Length < 2 || !long.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out long kb)) continue;
                    long bytes = kb * 1024;
                    switch (f[0])
                    {
                        case "MemTotal:": memTotal = bytes; break;
                        case "MemAvailable:": memAvail = bytes; break;
                        case "MemFree:": memFree = bytes; break;
                        case "Buffers:": buffers = bytes; break;
                        case "Cached:": cached = bytes; break;
                        case "SReclaimable:": sreclaim = bytes; break;
                    }
                }
                else if (line.StartsWith("@D ", StringComparison.Ordinal)) dfLine = line.Substring(3); // 마지막 줄 = 데이터
            }

            // CPU: 이번 호출의 마지막 샘플과 그 이전 샘플(첫 호출은 같은 명령 안의 0.3초 전 샘플)의 차이
            ulong[] latest = c2 ?? c1;
            if (latest != null)
            {
                lock (_cpuLock)
                {
                    ulong[] prev = c2 != null ? c1 : _prevCpu;
                    if (prev != null && latest[0] > prev[0] && latest[1] >= prev[1])
                    {
                        double dt = latest[0] - prev[0];
                        double di = latest[1] >= prev[1] ? latest[1] - prev[1] : 0;
                        st.CpuPercent = Math.Max(0, Math.Min(100, (dt - di) * 100.0 / dt));
                    }
                    _prevCpu = latest;
                }
            }

            // RAM: used = MemTotal - MemAvailable (없으면 free + buffers + cache 를 가용으로 본다)
            if (memTotal.HasValue)
            {
                long? avail = memAvail;
                if (!avail.HasValue && memFree.HasValue)
                    avail = memFree.Value + (buffers ?? 0) + (cached ?? 0) + (sreclaim ?? 0);
                if (avail.HasValue)
                {
                    st.MemTotal = memTotal;
                    st.MemUsed = Math.Max(0, memTotal.Value - avail.Value);
                }
            }

            // 디스크: df -Pk "Filesystem 1024-blocks Used Available Capacity Mounted on"
            if (dfLine != null)
            {
                string[] f = dfLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (f.Length >= 6
                    && long.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out long total)
                    && long.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out long used))
                {
                    st.DiskTotal = total * 1024;
                    st.DiskUsed = used * 1024;
                    st.DiskMount = string.Join(" ", f, 5, f.Length - 5);   // 공백 포함 마운트 경로
                }
            }
            return st;
        }

        /// <summary>/proc/stat 의 "cpu  user nice system idle iowait irq softirq steal ..." → {전체, 유휴}</summary>
        private static ulong[] ParseCpu(string line)
        {
            string[] f = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 5 || f[0] != "cpu") return null;
            ulong total = 0, idle = 0;
            // guest/guest_nice(9,10번째)는 user/nice 에 이미 포함되어 있으므로 8개까지만 더한다.
            for (int i = 1; i < f.Length && i <= 8; i++)
            {
                if (!ulong.TryParse(f[i], NumberStyles.None, CultureInfo.InvariantCulture, out ulong v)) return null;
                total += v;
                if (i == 4 || i == 5) idle += v;   // idle + iowait
            }
            return new[] { total, idle };
        }

        /// <summary>스크립트를 /bin/sh 로 실행해 표준출력을 돌려준다. 실패/시간 초과 시 null. 여러 스레드에서 동시 호출 가능.</summary>
        private string Run(string script)
        {
            try
            {
                var client = _client;
                if (client == null || !client.IsConnected) return null;
                using (var cmd = client.CreateCommand("/bin/sh -c '" + script + "'"))
                {
                    cmd.CommandTimeout = Timeout;
                    cmd.Execute();
                    return cmd.Result;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
