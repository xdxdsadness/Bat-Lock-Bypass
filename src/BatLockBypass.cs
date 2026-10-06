// Bat Lock Bypass — control window for the DPI bypass.
// Built with the C# compiler built into Windows (see tools/build_gui.bat).
// The manifest requests administrator rights: one UAC prompt when the exe starts,
// afterwards the engine starts directly, with no extra prompts.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

[assembly: System.Reflection.AssemblyTitle("Bat Lock Bypass")]
[assembly: System.Reflection.AssemblyProduct("Bat Lock Bypass")]
[assembly: System.Reflection.AssemblyDescription("DPI bypass for YouTube, SoundCloud, Discord")]
[assembly: System.Reflection.AssemblyCompany("BatPlayer")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.0.0")]

namespace BatLockBypass
{
    public class Preset
    {
        public string name { get; set; }
        public string args { get; set; }
    }

    // Result of a single HTTPS probe: whether any response arrived, and after how many ms.
    class ProbeResult
    {
        public bool Ok;
        public long Ms;
    }

    // Result of checking one bypass preset.
    class PresetScore : IComparable<PresetScore>
    {
        public int Index;
        public string Name;
        public bool EngineStarted;
        public int OkCount;
        public int Total;
        public long AvgMs = -1;

        // Better is the one with more reachable hosts; on a tie — lower latency.
        public int CompareTo(PresetScore other)
        {
            if (other == null) return -1;
            if (OkCount != other.OkCount) return OkCount > other.OkCount ? -1 : 1;
            long a = AvgMs < 0 ? long.MaxValue : AvgMs;
            long b = other.AvgMs < 0 ? long.MaxValue : other.AvgMs;
            if (a != b) return a < b ? -1 : 1;
            return Index.CompareTo(other.Index);
        }
    }

    public static class Program
    {
        static string baseDir;
        static string binDir;
        static string exePath;
        static List<Preset> presets;

        static Form form;
        static Label lblStatus;
        static Button btnToggle;
        static Button btnCheckAll;
        static ComboBox cmbPresets;
        static bool busy;
        static int runningPreset = -1;
        static int lastLoggedState = -1;   // 0=stopped, 1=running — for the log
        static string logPath;

        // Probe hosts used to score a preset — real targets from lists/hosts.txt,
        // including the hosts without which YouTube shows "no connection".
        static readonly string[] TestHosts =
        {
            "www.youtube.com", "youtubei.googleapis.com", "i.ytimg.com", "soundcloud.com", "api.soundcloud.com", "discord.com"
        };

        // Engine process + its plain copy (for stopping).
        static readonly string[] ProcNames = { "Bat lock bypass", "engine" };

        static volatile bool scanRunning;
        static volatile bool scanCancel;

        static void Log(string msg)
        {
            try
            {
                File.AppendAllText(logPath,
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + msg + "\r\n");
            }
            catch { }
        }

        [STAThread]
        static void Main()
        {
            baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            binDir = Path.Combine(baseDir, "bin");
            exePath = Path.Combine(binDir, "Bat lock bypass.exe");
            if (!File.Exists(exePath)) exePath = Path.Combine(binDir, "engine.exe");

            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BatLockBypass");
            try { Directory.CreateDirectory(logDir); } catch { }
            logPath = Path.Combine(logDir, "gui.log");
            Log("=== GUI start, baseDir=" + baseDir + ", exe exists=" + File.Exists(exePath));

            // Host probing goes over HTTPS — older systems need explicit TLS 1.2.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }

            try
            {
                var serializer = new JavaScriptSerializer();
                presets = serializer.Deserialize<List<Preset>>(
                    File.ReadAllText(Path.Combine(baseDir, "presets.json")));
            }
            catch (Exception ex)
            {
                Log("presets.json load error: " + ex.Message);
                presets = null;
            }

            if (presets == null || presets.Count == 0)
            {
                MessageBox.Show("Не удалось прочитать presets.json рядом с программой.",
                    "Bat Lock Bypass", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Log("presets loaded: " + presets.Count);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            form = new Form
            {
                Text = "Bat Lock Bypass",
                ClientSize = new Size(440, 318),
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                StartPosition = FormStartPosition.CenterScreen,
                Font = new Font("Segoe UI", 10f),
                AutoScaleMode = AutoScaleMode.Font,
            };
            try
            {
                form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            lblStatus = new Label
            {
                Location = new Point(24, 16),
                Size = new Size(392, 32),
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Text = "Проверяю...",
            };
            form.Controls.Add(lblStatus);

            btnToggle = new Button
            {
                Location = new Point(24, 56),
                Size = new Size(392, 44),
            };
            btnToggle.Click += delegate { ToggleBypass(); };
            form.Controls.Add(btnToggle);

            btnCheckAll = new Button
            {
                Location = new Point(24, 108),
                Size = new Size(392, 34),
                Text = "Проверить все способы (перебор + рейтинг)",
            };
            btnCheckAll.Click += delegate { OnCheckAllClick(); };
            form.Controls.Add(btnCheckAll);

            var lblPreset = new Label
            {
                Location = new Point(24, 152),
                Size = new Size(392, 22),
                Text = "Способ обхода (если не открывается — выбери другой):",
            };
            form.Controls.Add(lblPreset);

            cmbPresets = new ComboBox
            {
                Location = new Point(24, 176),
                Size = new Size(392, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                MaxDropDownItems = 8,
            };
            for (int i = 0; i < presets.Count; i++)
                cmbPresets.Items.Add((i + 1) + ". " + presets[i].name);
            if (cmbPresets.Items.Count > 0) cmbPresets.SelectedIndex = 0;
            cmbPresets.SelectedIndexChanged += delegate
            {
                if (busy || scanRunning) return;
                if (IsRunning())
                {
                    busy = true;
                    try { StartPreset(cmbPresets.SelectedIndex); }
                    finally { busy = false; }
                }
                UpdateStatus();
            };
            form.Controls.Add(cmbPresets);

            var lblHint = new Label
            {
                Location = new Point(24, 212),
                Size = new Size(392, 96),
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8.5f),
                Text = "«Проверить все способы» по очереди запускает каждый, меряет доступность " +
                       "и задержку сайтов и показывает рейтинг — лучший можно включить одним кликом.\n\n" +
                       "Обход работает в фоне, пока включён. Если закрыть это окно — он не отключится.",
            };
            form.Controls.Add(lblHint);

            var timer = new System.Windows.Forms.Timer { Interval = 2000 };
            timer.Tick += delegate { UpdateStatus(); };
            timer.Start();

            form.FormClosing += delegate
            {
                if (scanRunning)
                {
                    scanCancel = true;
                    StopBypass();
                    return;
                }
                if (!IsRunning()) return;
                var answer = MessageBox.Show(form,
                    "Остановить обход перед выходом?",
                    "Bat Lock Bypass",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer == DialogResult.Yes) StopBypass();
            };

            // Auto-start: if the bypass is not running yet — enable preset 1.
            Log("startup IsRunning=" + IsRunning());
            if (!IsRunning())
            {
                lblStatus.Text = "Запускаю обход...";
                lblStatus.ForeColor = Color.DimGray;
                Application.DoEvents();
                busy = true;
                try { StartPreset(0); }
                finally { busy = false; }
            }
            UpdateStatus();

            Application.Run(form);
        }

        // The GUI process name matches the engine's branded name — exclude our own PID.
        static bool IsRunning()
        {
            int myPid = Process.GetCurrentProcess().Id;
            foreach (var name in ProcNames)
                foreach (var p in Process.GetProcessesByName(name))
                    if (p.Id != myPid) return true;
            return false;
        }

        static void StopBypass()
        {
            int myPid = Process.GetCurrentProcess().Id;
            foreach (var name in ProcNames)
                foreach (var p in Process.GetProcessesByName(name))
                    if (p.Id != myPid)
                    {
                        try { p.Kill(); } catch { }
                    }
            runningPreset = -1;
        }

        static void ToggleBypass()
        {
            if (busy || scanRunning) return;
            busy = true;
            try
            {
                if (IsRunning())
                {
                    StopBypass();
                }
                else
                {
                    int idx = cmbPresets.SelectedIndex >= 0 ? cmbPresets.SelectedIndex : 0;
                    StartPreset(idx);
                }
            }
            finally { busy = false; }
            UpdateStatus();
        }

        // Starts the engine without touching the UI — also used from the scan thread.
        static bool StartEngineCore(int index)
        {
            if (index < 0 || index >= presets.Count) return false;
            StopBypass();
            Thread.Sleep(400);

            var arguments = presets[index].args.Replace("{BASE}", baseDir);
            Log("start engine preset " + (index + 1) + " [" + presets[index].name + "] exe=" + exePath);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = binDir,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            try
            {
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log("Process.Start ERROR: " + ex.Message);
                runningPreset = -1;
                return false;
            }

            // Wait for the process to appear (up to 8 seconds).
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < deadline)
            {
                if (IsRunning())
                {
                    runningPreset = index;
                    return true;
                }
                Thread.Sleep(300);
            }
            Log("engine did NOT appear within 8s (preset " + (index + 1) + ")");
            runningPreset = -1;
            return false;
        }

        static void StartPreset(int index)
        {
            bool ok = StartEngineCore(index);
            UpdateStatus();
            if (!ok)
            {
                MessageBox.Show("Обход не запустился (процесс сразу завершился).\n" +
                    "Попробуй другой способ обхода в списке.",
                    "Bat Lock Bypass", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // A single HTTPS probe. Any server response (even 401/403) counts as success:
        // the TLS handshake made it through and the bypass works. Otherwise — timeout/reset.
        static ProbeResult ProbeHost(string host, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://" + host + "/");
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.AllowAutoRedirect = false;
                req.UserAgent = "Mozilla/5.0";
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    sw.Stop();
                    return new ProbeResult { Ok = true, Ms = sw.ElapsedMilliseconds };
                }
            }
            catch (WebException ex)
            {
                sw.Stop();
                if (ex.Response != null)
                {
                    try { ex.Response.Close(); } catch { }
                    return new ProbeResult { Ok = true, Ms = sw.ElapsedMilliseconds };
                }
                return new ProbeResult { Ok = false, Ms = sw.ElapsedMilliseconds };
            }
            catch
            {
                return new ProbeResult { Ok = false, Ms = sw.ElapsedMilliseconds };
            }
        }

        // Tests one preset: start engine -> parallel probes -> stop.
        static PresetScore TestPreset(int index)
        {
            var score = new PresetScore
            {
                Index = index,
                Name = presets[index].name,
                Total = TestHosts.Length,
            };
            if (!StartEngineCore(index))
            {
                StopBypass();
                return score;
            }
            score.EngineStarted = true;
            Thread.Sleep(700);   // the engine needs time to warm up

            var results = new ProbeResult[TestHosts.Length];
            Parallel.For(0, TestHosts.Length, new ParallelOptions
            {
                MaxDegreeOfParallelism = TestHosts.Length
            }, i =>
            {
                var r = ProbeHost(TestHosts[i], 3500);
                if (!r.Ok) r = ProbeHost(TestHosts[i], 3500);   // one retry
                results[i] = r;
            });

            long sum = 0;
            int ok = 0;
            foreach (var r in results)
                if (r != null && r.Ok) { ok++; sum += r.Ms; }
            score.OkCount = ok;
            if (ok > 0) score.AvgMs = sum / ok;

            StopBypass();
            return score;
        }

        static void OnCheckAllClick()
        {
            if (busy) return;
            if (scanRunning)
            {
                // Second click — soft cancel: the current preset finishes and the loop breaks.
                scanCancel = true;
                btnCheckAll.Text = "Останавливаю...";
                return;
            }
            busy = true;
            try
            {
                scanCancel = false;
                scanRunning = true;
                btnToggle.Enabled = false;
                cmbPresets.Enabled = false;
                btnCheckAll.Text = "Остановить проверку";
                UpdateStatus();
                var t = new Thread(ScanThread) { IsBackground = true };
                t.Start();
            }
            finally { busy = false; }
        }

        static void ScanThread()
        {
            Log("scan: started, presets=" + presets.Count);
            var scores = new List<PresetScore>();
            for (int i = 0; i < presets.Count; i++)
            {
                if (scanCancel) break;
                int idx = i;
                try
                {
                    form.BeginInvoke((MethodInvoker)delegate
                    {
                        lblStatus.Text = "Проверка " + (idx + 1) + "/" + presets.Count + ": " + presets[idx].name;
                        lblStatus.ForeColor = Color.DarkOrange;
                    });
                }
                catch { }
                var s = TestPreset(idx);
                scores.Add(s);
                Log("scan: preset " + (idx + 1) + " [" + s.Name + "] ok=" + s.OkCount + "/" + s.Total +
                    " avg=" + s.AvgMs + "ms engineStarted=" + s.EngineStarted);
            }
            StopBypass();
            Log("scan: finished, tested=" + scores.Count + ", cancelled=" + scanCancel);
            try
            {
                form.BeginInvoke((MethodInvoker)delegate { ShowScanResults(scores); });
            }
            catch { }
        }

        static void ShowScanResults(List<PresetScore> scores)
        {
            scanRunning = false;
            scanCancel = false;
            btnToggle.Enabled = true;
            cmbPresets.Enabled = true;
            btnCheckAll.Text = "Проверить все способы (перебор + рейтинг)";
            runningPreset = -1;

            scores.Sort();
            UpdateStatus();

            var dlg = new Form
            {
                Text = "Результаты проверки способов",
                ClientSize = new Size(544, 452),
                FormBorderStyle = FormBorderStyle.FixedSingle,
                MaximizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                Font = new Font("Segoe UI", 9.5f),
                ShowInTaskbar = false,
            };

            var lv = new ListView
            {
                Location = new Point(12, 12),
                Size = new Size(520, 336),
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
            };
            lv.Columns.Add("Место", 56);
            lv.Columns.Add("Способ", 276);
            lv.Columns.Add("Доступно", 100);
            lv.Columns.Add("Задержка", 82);
            for (int i = 0; i < scores.Count; i++)
            {
                var s = scores[i];
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add((s.Index + 1) + ". " + s.Name);
                item.SubItems.Add(s.EngineStarted ? s.OkCount + " из " + s.Total : "не запустился");
                item.SubItems.Add(s.OkCount > 0 ? s.AvgMs + " мс" : "—");
                if (i == 0 && s.OkCount > 0)
                {
                    item.BackColor = Color.Honeydew;
                    item.ForeColor = Color.ForestGreen;
                }
                else if (s.OkCount == 0)
                {
                    item.ForeColor = Color.Gray;
                }
                lv.Items.Add(item);
            }
            dlg.Controls.Add(lv);

            int best = -1;
            for (int i = 0; i < scores.Count; i++)
                if (scores[i].OkCount > 0) { best = i; break; }

            var lblSummary = new Label
            {
                Location = new Point(12, 354),
                Size = new Size(520, 40),
                ForeColor = Color.DimGray,
            };
            if (best < 0)
                lblSummary.Text = "Ни один способ не дал доступа. Проверь интернет и запусти проверку ещё раз.";
            else
                lblSummary.Text = "Лучший: " + (scores[best].Index + 1) + ". " + scores[best].Name +
                    "  (" + scores[best].OkCount + " из " + scores[best].Total + ", ~" + scores[best].AvgMs + " мс).\n" +
                    "Двойной клик по строке — включить этот способ.";
            dlg.Controls.Add(lblSummary);

            var btnBest = new Button
            {
                Location = new Point(12, 404),
                Size = new Size(254, 32),
                Text = "Включить лучший",
                Enabled = best >= 0,
            };
            var btnClose = new Button
            {
                Location = new Point(278, 404),
                Size = new Size(254, 32),
                Text = "Закрыть",
            };
            btnBest.Click += delegate
            {
                if (best < 0) return;
                ApplyPreset(scores[best].Index);
                dlg.Close();
            };
            lv.DoubleClick += delegate
            {
                if (lv.SelectedIndices.Count == 0) return;
                ApplyPreset(scores[lv.SelectedIndices[0]].Index);
                dlg.Close();
            };
            btnClose.Click += delegate { dlg.Close(); };
            dlg.Controls.Add(btnBest);
            dlg.Controls.Add(btnClose);

            dlg.ShowDialog(form);
        }

        // Applies the preset by index and syncs the dropdown.
        static void ApplyPreset(int index)
        {
            if (index < 0 || index >= presets.Count) return;
            busy = true;
            try
            {
                if (cmbPresets.SelectedIndex != index) cmbPresets.SelectedIndex = index;
                StartPreset(index);
            }
            finally { busy = false; }
            UpdateStatus();
        }

        static void UpdateStatus()
        {
            if (scanRunning) return;   // the scan thread owns the status line
            bool running = IsRunning();
            int state = running ? 1 : 0;
            if (state != lastLoggedState)
            {
                Log("state -> " + (running ? "RUNNING" : "STOPPED"));
                lastLoggedState = state;
            }
            if (running)
            {
                lblStatus.Text = "● Обход работает" +
                    (runningPreset >= 0 ? "  (способ " + (runningPreset + 1) + ")" : "");
                lblStatus.ForeColor = Color.ForestGreen;
                btnToggle.Text = "Выключить обход";
            }
            else
            {
                lblStatus.Text = "● Обход выключен";
                lblStatus.ForeColor = Color.Firebrick;
                btnToggle.Text = "Включить обход";
            }
        }
    }
}
