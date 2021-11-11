using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OBSWebsocketDotNet;
using LiveSplit.ComponentUtil;
using System.Reflection;
using Microsoft.VisualBasic;
using System.Threading;
using System.Threading.Tasks;

namespace Auto_Recorder
{
    class Program
    {
        private static MainForm MainForm = new MainForm();
        private static OBSWebsocket obs = new OBSWebsocket();
        private static Process game;
        private static Watchers watchers;

        private static void Main()
        {
            obs.WSTimeout = TimeSpan.FromSeconds(1);
            Task task = new Task(() => AutoRecordTask());
            task.Start();
            Application.EnableVisualStyles();
            Application.Run(MainForm);
        }

        private static void AutoRecordTask()
        {
            while (true)
            {
                Thread.Sleep(15);

                if (game == null || game.HasExited)
                {
                    if (!HookGameProcess())
                    {
                        MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label3.Text = "Auto recording disabled: couldn't connect to the game!"; });
                        MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label4.Text = "Currently not recording"; });
                        continue;
                    }
                }

                if (!obs.IsConnected)
                {
                    try
                    {
                        obs.Connect("ws://127.0.0.1:4444", "");
                        MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label3.Text = "Auto recording enabled!"; });
                        continue;
                    }
                    catch
                    {
                        MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label3.Text = "Auto recording disabled: couldn't connect to OBS!"; });
                        continue;
                    }
                }

                try
                {
                    obs.GetStreamingStatus();
                }
                catch
                {
                    var question = Interaction.InputBox("OBS WebSocket requires a password.\n\nPlease inpput the password and click OK\nor click \"Cancel\" to exit the program.", "TSR Auto Recorder", "password");

                    if (question == "") Environment.Exit(0);

                    try
                    {
                        obs.Authenticate(question, obs.GetAuthInfo());
                        MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label3.Text = "Auto recording enabled!"; });
                    }
                    catch (AuthFailureException)
                    {
                        obs.Disconnect();
                    }
                    continue;
                }

                watchers.UpdateAll(game);

                if (obs.GetStreamingStatus().IsRecording)
                {
                    if (watchers.raceFinish1.Current == 1 || watchers.raceFinish2.Current == 1 || watchers.raceState.Current < 5) StopRecording();
                }
                else if (watchers.raceState.Current == 5 || watchers.raceState.Current == 6)
                {
                    StartRecording();
                }

                switch (obs.GetStreamingStatus().IsRecording)
                {
                    case true:
                        MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label4.Text = "Recording!"; });
                        break;
                    case false:
                        MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label4.Text = "Currently not recording"; });
                        break;
                }
            }
        }

        private static void StartRecording()
        {
            if (!obs.GetStreamingStatus().IsRecording)
            {
                obs.StartRecording();
            }
        }

        private static void StopRecording()
        {
            if (obs.GetStreamingStatus().IsRecording)
            {
                obs.StopRecording();
            }
        }
             
        private static bool HookGameProcess()
        {
            game = Process.GetProcessesByName("GameApp_PcDx11_x64Final").OrderByDescending(x => x.StartTime).FirstOrDefault(x => !x.HasExited);
            if (game == null) return false;
            
            try
            {
                watchers = new Watchers(game);
            }
            catch
            {
                game = null;
                return false;
            }
            return true;
        }
    }

    class Watchers : MemoryWatcherList
    {
        public MemoryWatcher<byte> raceState { get; }
        public MemoryWatcher<byte> raceFinish1 { get; }
        public MemoryWatcher<byte> raceFinish2 { get; }

        public Watchers(Process game)
        {
            var scanner = new SignatureScanner(game, game.MainModuleWow64Safe().BaseAddress, game.MainModuleWow64Safe().ModuleMemorySize);
            IntPtr ptr;

            ptr = scanner.Scan(new SigScanTarget(1, "74 72 83 BE")); if (ptr == IntPtr.Zero) throw new Exception();
            ptr = ptr + game.ReadValue<byte>(ptr) + 0x1 + 0x2;
            this.raceState = new MemoryWatcher<byte>(new DeepPointer(ptr + 4 + game.ReadValue<int>(ptr))) { FailAction = MemoryWatcher.ReadFailAction.SetZeroOrNull };

            ptr = scanner.Scan(new SigScanTarget(10, "48 8D 15 ???????? 48 8D 0D ???????? E8 ???????? 84 C0 0F 84 ???????? C7 46")); if (ptr == IntPtr.Zero) throw new Exception();
            this.raceFinish1 = new MemoryWatcher<byte>(new DeepPointer(ptr + 4 + game.ReadValue<int>(ptr) + 0x4)) { FailAction = MemoryWatcher.ReadFailAction.SetZeroOrNull };

            ptr = scanner.Scan(new SigScanTarget(10, "48 8B 15 ???????? 48 8D 0D ???????? E8 ???????? 84 C0 75 08 49 8B CE E8 ???????? 49 C7 46 ?????????? 66 0F 6F 0D")); if (ptr == IntPtr.Zero) throw new Exception();
            this.raceFinish2 = new MemoryWatcher<byte>(new DeepPointer(ptr + 4 + game.ReadValue<int>(ptr) + 0x4)) { FailAction = MemoryWatcher.ReadFailAction.SetZeroOrNull };

            this.AddRange(this.GetType().GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Where(p => !p.GetIndexParameters().Any()).Select(p => p.GetValue(this, null) as MemoryWatcher).Where(p => p != null));
        }
    }

}