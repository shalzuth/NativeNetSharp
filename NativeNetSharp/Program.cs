using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.IO;
using Microsoft.Win32.SafeHandles;

namespace NativeNetSharp
{
    public unsafe static class Program
    {
        public static void DllMain()
        {
        }
        public static Int32 Main()
        {
            if (Process.GetCurrentProcess().ProcessName == typeof(Program).Namespace) NativeNetSharp.Inject("notepad", File.ReadAllBytes(System.Reflection.Assembly.GetEntryAssembly().Location));
            else new System.Threading.Thread(() =>
            {
                try
                {
                    NativeNetSharp.AllocConsole();
                    var standardOutput = new StreamWriter(new FileStream(new SafeFileHandle(NativeNetSharp.GetStdHandle(-11), true), FileAccess.Write), Encoding.GetEncoding(437)) { AutoFlush = true };
                    Console.SetOut(standardOutput);
                    Console.WriteLine("C# DLL loaded");
                    DllMain();
                    MessageBox.Show("c# win form", Process.GetCurrentProcess().ProcessName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception e)
                {
                    MessageBox.Show("err" + " : " + e.Message, Process.GetCurrentProcess().ProcessName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            ).Start();
            return 0;
        }
    }
}
