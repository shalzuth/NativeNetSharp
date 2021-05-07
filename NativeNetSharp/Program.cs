using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NativeNetSharp
{
    public static class Program
    {
        public static Int32 Main()
        {
            if (Process.GetCurrentProcess().ProcessName == typeof(Program).Namespace) Inject("notepad");
            else new System.Threading.Thread(() => MessageBox.Show("c# win form", Process.GetCurrentProcess().ProcessName, MessageBoxButtons.OK, MessageBoxIcon.Information)).Start();
            return 0;
        }
        public static IntPtr baseAddress;
        public static IntPtr procHandle;
        public static void Inject(String procName)
        {
            var CLSID_CLRMetaHost = new Guid("9280188D-0E8E-4867-B30C-7FA83884E8DE").ToByteArray();
            var IID_ICLRMetaHost = new Guid("D332DB9E-B9B3-4125-8207-A14884F53216").ToByteArray();
            var IID_ICLRRuntimeInfo = new Guid("BD39D1D2-BA2F-486A-89B0-B4B0CB466891").ToByteArray();
            var CLSID_CorRuntimeHost = new Guid("CB2F6723-AB3A-11D2-9C40-00C04FA30A3E").ToByteArray();
            var IID_ICorRuntimeHost = new Guid("CB2F6722-AB3A-11D2-9C40-00C04FA30A3E").ToByteArray();
            var _AppDomain = new Guid("05F696DC-2B29-3663-AD8B-C4389CF2A713").ToByteArray();
            var CLSID_CLRRuntimeHost = new Guid("90F1A06E-7712-4762-86B5-7A5EBA6BDB02").ToByteArray();
            var IID_ICLRRuntimeHost = new Guid("90F1A06C-7712-4762-86B5-7A5EBA6BDB02").ToByteArray();

            LoadLibrary("oleaut32.dll"); // needed for SafeArray addresses
            var targetProcess = Process.GetProcessesByName("notepad")[0];
            baseAddress = targetProcess.MainModule.BaseAddress;
            procHandle = OpenProcess(0x43a, false, targetProcess.Id);
            RemoteLoadLibrary("mscoree.dll");
            RemoteLoadLibrary("oleaut32.dll"); // todo remove safe array structure dependency

            var exeBytes = System.IO.File.ReadAllBytes(System.Reflection.Assembly.GetEntryAssembly().Location);
            var bounds = BitConverter.GetBytes(exeBytes.Length).ToList(); bounds.AddRange(BitConverter.GetBytes((UInt64)0));
            var exeSafeArray = BitConverter.GetBytes(ExecFunc(GetProcAddress(GetModuleHandle("oleaut32.dll"), "SafeArrayCreate"), BitConverter.GetBytes((UInt64)0x11), BitConverter.GetBytes((UInt64)1), bounds.ToArray()));
            var exeInTargetMem = ExecFunc(GetProcAddress(GetModuleHandle("oleaut32.dll"), "SafeArrayAccessData"), exeSafeArray, new Byte[0]);
            WriteProcessMemory(procHandle, (IntPtr)exeInTargetMem, exeBytes, exeBytes.Length, out _);
            ExecFunc(GetProcAddress(GetModuleHandle("oleaut32.dll"), "SafeArrayUnaccessData"), exeSafeArray);
            var metaHost = ExecFunc(GetProcAddress(GetModuleHandle("mscoree.dll"), "CLRCreateInstance"), CLSID_CLRMetaHost, IID_ICLRMetaHost, new Byte[0]);
            var runtime = ExecVTable(metaHost, 0x18, Encoding.Unicode.GetBytes("v4.0.30319"), IID_ICLRRuntimeInfo, new Byte[0]);
            var runtimeHost = ExecVTable(runtime, 0x48, CLSID_CorRuntimeHost, IID_ICorRuntimeHost, new Byte[0]);
            var started = ExecVTable(runtimeHost, 0x50);
            var domain = ExecVTable(runtimeHost, 0x68, new Byte[0]);
            var appDomain = ExecVTable(domain, 0, _AppDomain, new Byte[0]);
            var assembly = ExecVTable(appDomain, 0x168, exeSafeArray, new Byte[0]);
            var method = ExecVTable(assembly, 0x80, new Byte[0]);
            var variant = new Byte[0x18]; variant[0] = 1;
            var mainReturnVal = new Byte[0x18];
            var methodResult = ExecVTable(method, 0x128, variant, new Byte[8], mainReturnVal);
            var val = BitConverter.ToUInt64(mainReturnVal, 8);
            var released = ExecVTable(method, 0x10); // not sure how this works if the app is still running.
            ExecFunc(GetProcAddress(GetModuleHandle("oleaut32.dll"), "SafeArrayDestroy"), exeSafeArray, new Byte[0]); // not sure how this works if the app is still running.
            var stopped = ExecVTable(runtimeHost, 0x58); // not sure how this works if the app is still running.
        }
        [DllImport("kernel32")] static extern IntPtr OpenProcess(Int32 dwDesiredAccess, Boolean bInheritHandle, Int32 dwProcessId);
        [DllImport("kernel32")] static extern IntPtr GetModuleHandle(String lpModuleName);
        [DllImport("kernel32")] static extern IntPtr GetProcAddress(IntPtr hModule, String procName);
        [DllImport("kernel32")] static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, Int32 dwSize, UInt32 flAllocationType, UInt32 flProtect);
        [DllImport("kernel32")] static extern Int32 ReadProcessMemory(IntPtr hProcess, UInt64 lpBaseAddress, [In, Out] Byte[] buffer, Int32 size, out Int32 lpNumberOfBytesRead);
        [DllImport("kernel32")] static extern Boolean WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, Byte[] lpBuffer, Int32 nSize, out Int32 lpNumberOfBytesWritten);
        [DllImport("kernel32")] static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, UInt32 dwStackSize, IntPtr lpStartAddress, IntPtr lpParameter, UInt32 dwCreationFlags, IntPtr lpThreadId);
        [DllImport("kernel32")] static extern UInt32 WaitForSingleObject(IntPtr hHandle, UInt32 dwMilliseconds);
        [DllImport("kernel32")] static extern Int32 CloseHandle(IntPtr hObject);
        [DllImport("kernel32")] static extern Boolean VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, Int32 dwSize, UInt32 flNewProtect, out UInt32 lpflOldProtect);
        [DllImport("kernel32")] static extern Boolean VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, Int32 dwSize, Int32 dwFreeType);
        [DllImport("kernel32")] static extern IntPtr LoadLibrary(String lpFileName);
        public static UInt64 ExecVTable(UInt64 obj, UInt64 offset, params Byte[][] args)
        {
            var methodAddr = ReadUInt64(ReadUInt64(obj) + offset);
            args = args.Prepend(BitConverter.GetBytes(obj)).ToArray();
            return ExecFunc((IntPtr)methodAddr, args);
        }
        public static UInt64 ExecFunc(IntPtr funcAddr, params Byte[][] args)
        {
            var newArgs = new List<IntPtr>();
            foreach(var arg in args)
            {
                if (arg.Length == 8) newArgs.Add((IntPtr)BitConverter.ToUInt64(arg, 0)); // todo fix hack for direct args
                else
                {
                    var argLength = arg.Length == 0 ? 0x8 : arg.Length;
                    var temp = VirtualAllocEx(procHandle, IntPtr.Zero, argLength, 0x3000, 0x40);
                    WriteProcessMemory(procHandle, temp, arg, argLength, out _);
                    newArgs.Add(temp);
                }
            }
            var retVal = ExecFunc(funcAddr, newArgs.ToArray());
            for(var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                var argLength = arg.Length == 0 ? 0x8 : arg.Length;
                var buf = new Byte[argLength];
                if (args[i].Length == 8) { }
                else
                {
                    ReadProcessMemory(procHandle, (UInt64)newArgs[i], buf, buf.Length, out _);
                    if (args.Length != argLength) Array.Resize(ref args[i], argLength);
                    Array.Copy(buf, args[i], argLength);
                    VirtualFreeEx(procHandle, newArgs[i], 0, 0x8000);
                }
            }
            if (retVal == 0 && args.ToList().Last().Length == 8) return BitConverter.ToUInt64(args.ToList().Last(), 0); // todo fix hack for arg refs
            else return retVal;
        }
        public static UInt64 ExecFunc(IntPtr funcAddr, params IntPtr[] args)
        {
            var asm = new List<Byte>();
            var retVal = VirtualAllocEx(procHandle, IntPtr.Zero, 8, 0x3000, 4);
            WriteProcessMemory(procHandle, retVal, BitConverter.GetBytes(0xdeadbeefcafef00d), 8, out _);
            asm.AddRange(new Byte[] { 0x48, 0x83, 0xEC, 0x38 }); // sub rsp 0x38
            for (var i = 0; i < args.Length && i < 4; i++)
            {
                if (i == 0) asm.AddRange(new Byte[] { 0x48, 0xB9 }); // mov rcx
                if (i == 1) asm.AddRange(new Byte[] { 0x48, 0xBA }); // mov rdx
                if (i == 2) asm.AddRange(new Byte[] { 0x49, 0xB8 }); // mov r8
                if (i == 3) asm.AddRange(new Byte[] { 0x49, 0xB9 }); // mov r9
                asm.AddRange(BitConverter.GetBytes((UInt64)args[i]));
            }
            for (var i = 4; i < args.Length; i++) // broke need to fix
            {
                asm.Add(0x68);
                asm.AddRange(BitConverter.GetBytes((UInt32)(UInt64)args[i]));
                asm.Add(0x68);
                asm.AddRange(BitConverter.GetBytes(((UInt64)args[i]) >> 32));
            }
            asm.AddRange(new Byte[] { 0x48, 0xB8 }); // mov rax
            asm.AddRange(BitConverter.GetBytes((UInt64)funcAddr));

            asm.AddRange(new Byte[] { 0xFF, 0xD0 }); // call rax
            asm.AddRange(new Byte[] { 0x48, 0x83, 0xC4, 0x38 }); // add rsp 0x38

            asm.AddRange(new Byte[] { 0x48, 0xA3 }); // mov rax to retval
            asm.AddRange(BitConverter.GetBytes((UInt64)retVal));
            asm.Add(0xC3); // ret
            var codePtr = VirtualAllocEx(procHandle, IntPtr.Zero, asm.Count, 0x3000, 0x40);
            WriteProcessMemory(procHandle, codePtr, asm.ToArray(), asm.Count, out _);
            var thread = CreateRemoteThread(procHandle, IntPtr.Zero, 0, codePtr, IntPtr.Zero, 0, IntPtr.Zero);
            WaitForSingleObject(thread, 10000);
            var buf = new Byte[8];
            ReadProcessMemory(procHandle, (UInt64)retVal, buf, buf.Length, out _);
            VirtualFreeEx(procHandle, retVal, 0, 0x8000);
            VirtualFreeEx(procHandle, codePtr, 0, 0x8000);
            CloseHandle(thread);
            return BitConverter.ToUInt64(buf, 0);
        }
        public static UInt64 ReadUInt64(UInt64 addr)
        {
            var temp = new Byte[8];
            ReadProcessMemory(procHandle, addr, temp, temp.Length, out _);
            return BitConverter.ToUInt64(temp, 0);
        }
        public static void RemoteLoadLibrary(String dllName)
        {
            var loadLibraryAddr = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");
            var allocMemAddress = VirtualAllocEx(procHandle, IntPtr.Zero, ((dllName.Length + 1) * Marshal.SizeOf(typeof(char))), 0x3000, 4);
            WriteProcessMemory(procHandle, allocMemAddress, Encoding.Default.GetBytes(dllName), ((dllName.Length + 1) * Marshal.SizeOf(typeof(char))), out _);
            var thread = CreateRemoteThread(procHandle, IntPtr.Zero, 0, loadLibraryAddr, allocMemAddress, 0, IntPtr.Zero); WaitForSingleObject(thread, 10000);
            VirtualFreeEx(procHandle, allocMemAddress, 0, 0x8000);
        }
    }
}
