using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OBSWebsocketDotNet;

namespace Auto_Recorder
{
    static class Program
    {
        [DllImport("kernel32")]
        private static extern int OpenProcess(int dwDesiredAccess, int bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll")]
        private static extern bool ReadProcessMemory(int hProcess, Int64 lpBaseAddress, byte[] lpBuffer, int dwSize, int lpNumberOfBytesRead);

        static Process[] processes;
        static int handle = 0;
        static OBSWebsocket obs = new OBSWebsocket();
        static byte raceState;
        static byte raceFinish1;
        static byte raceFinish2;
        static bool recording;

        static MainForm MainForm = new MainForm();

        static void Main()
        {
            obs.RecordingStateChanged += Obs_RecordingStateChanged;
            obs.WSTimeout = TimeSpan.FromSeconds(2);

            Task task = new Task(() => AutoRecordTask());
            task.Start();

            Application.EnableVisualStyles();
            Application.Run(MainForm);
        }

        private static void AutoRecordTask()
        {
            while (true)
            {
                if (!obs.IsConnected)
                    try
                    {
                        obs.Connect("ws://127.0.0.1:4444", "");
                        recording = obs.GetStreamingStatus().IsRecording;
                    }
                    catch
                    { }

                processes = Process.GetProcessesByName("GameApp_PcDx11_x64Final");

                if (processes.Length == 0)
                {
                    handle = 0;
                    if (recording)
                        ToggleRecording();
                }
                else if (handle == 0)
                {
                    if (processes[0].MainModule.ModuleMemorySize != 0x15E9D000)
                    {
                        MessageBox.Show("You cannot use this autorecorder tool. Please ensure you are\n" +
                                        "running the correct version of the game!", "TSR Auto Recorder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        Environment.Exit(2);
                    }
                    Thread.Sleep(2000);
                    handle = OpenProcess(0x10, 0, processes[0].Id);
                }

                if (handle != 0)
                {
                    raceState = ReadByte(0x1410B1920);
                    raceFinish1 = ReadByte(0x141136234);
                    raceFinish2 = ReadByte(0x141129D24);

                    if (recording)
                    {
                        if (raceFinish1 == 1 || raceFinish2 == 1)
                            ToggleRecording();
                        if (raceState < 5)
                            ToggleRecording();
                    }
                    else if (raceState == 5 || raceState == 6)
                    {
                        Thread.Sleep(16);
                        ToggleRecording();
                    }

                }

                if (obs.IsConnected && handle != 0)
                    MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label3.Text = "Auto recording enabled!"; });
                else if (obs.IsConnected && handle == 0)
                    MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label3.Text = "Auto recording disabled: couldn't connect to the game!"; });
                else
                    MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label3.Text = "Auto recording disabled: couldn't connect to OBS!"; });

                if (recording)
                    MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label4.Text = "Recording!"; });
                else
                    MainForm.BeginInvoke((MethodInvoker)delegate () { MainForm.label4.Text = "currently not recording"; });

                Thread.Sleep(10);
            }
        }
             
        private static void Obs_RecordingStateChanged(OBSWebsocket sender, OutputState type)
        {
            recording = (type == OutputState.Starting || type == OutputState.Started);
        }

        static void ToggleRecording()
        {
            if (obs.IsConnected)
                obs.ToggleRecording();
        }

        static byte ReadByte(Int64 Address)
        {
            byte[] Bytes = new byte[1];
            ReadProcessMemory(handle, Address, Bytes, 1, 0);
            return Bytes[0];
        }
    }
}