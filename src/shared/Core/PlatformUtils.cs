using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using GitCredentialManager.Interop.Posix.Native;

namespace GitCredentialManager
{
    public static class PlatformUtils
    {
        /// <summary>
        /// Get information about the current platform (OS and CLR details).
        /// </summary>
        /// <returns>Platform information.</returns>
        public static PlatformInformation GetPlatformInformation()
        {
            string osType = GetOSType();
            string osVersion = GetOSVersion();
            string cpuArch = GetCpuArchitecture();
            string clrVersion = GetClrVersion();

            return new PlatformInformation(osType, osVersion, cpuArch, clrVersion);
        }

        public static bool IsWindowsBrokerSupported()
        {
            if (!IsWindows())
            {
                return false;
            }

            // Implementation of version checking was taken from:
            // https://github.com/dotnet/runtime/blob/6578f257e3be2e2144a65769706e981961f0130c/src/libraries/System.Private.CoreLib/src/System/Environment.Windows.cs#L110-L122
            //
            // Note that we cannot use Environment.OSVersion in .NET Framework (or Core versions less than 5.0) as
            // the implementation in those versions "lies" about Windows versions > 8.1 if there is no application manifest.
            if (RtlGetVersionEx(out RTL_OSVERSIONINFOEX osvi) != 0)
            {
                return false;
            }

            // Windows major version 10 is required for WAM
            if (osvi.dwMajorVersion < 10)
            {
                return false;
            }

            // Specific minimum build number is different between Windows Server and Client SKUs
            const int minClientBuildNumber = 15063;
            const int minServerBuildNumber = 17763; // Server 2019

            switch (osvi.wProductType)
            {
                case VER_NT_WORKSTATION:
                    return osvi.dwBuildNumber >= minClientBuildNumber;

                case VER_NT_SERVER:
                case VER_NT_DOMAIN_CONTROLLER:
                    return osvi.dwBuildNumber >= minServerBuildNumber;
            }

            return false;
        }

        /// <summary>
        /// Check if the current Operating System is macOS.
        /// </summary>
        /// <returns>True if running on macOS, false otherwise.</returns>
        public static bool IsMacOS()
        {
#if NETFRAMEWORK
            return Environment.OSVersion.Platform == PlatformID.MacOSX;
#elif NETSTANDARD
            return RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
#endif
        }

        /// <summary>
        /// Check if the current Operating System is Windows.
        /// </summary>
        /// <returns>True if running on Windows, false otherwise.</returns>
        public static bool IsWindows()
        {
#if NETFRAMEWORK
            return Environment.OSVersion.Platform == PlatformID.Win32NT;
#elif NETSTANDARD
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif
        }

        /// <summary>
        /// Check if the current Operating System is Linux-based.
        /// </summary>
        /// <returns>True if running on a Linux distribution, false otherwise.</returns>
        public static bool IsLinux()
        {
#if NETFRAMEWORK
            return Environment.OSVersion.Platform == PlatformID.Unix;
#elif NETSTANDARD
            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
#endif
        }

        /// <summary>
        /// Check if the current Operating System is POSIX-compliant.
        /// </summary>
        /// <returns>True if running on a POSIX-compliant Operating System, false otherwise.</returns>
        public static bool IsPosix()
        {
            return IsMacOS() || IsLinux();
        }

        /// <summary>
        /// Ensure the current Operating System is macOS, fail otherwise.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Thrown if the current OS is not macOS.</exception>
        public static void EnsureMacOS()
        {
            if (!IsMacOS())
            {
                throw new PlatformNotSupportedException();
            }
        }

        /// <summary>
        /// Ensure the current Operating System is Windows, fail otherwise.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Thrown if the current OS is not Windows.</exception>
        public static void EnsureWindows()
        {
            if (!IsWindows())
            {
                throw new PlatformNotSupportedException();
            }
        }

        /// <summary>
        /// Ensure the current Operating System is Linux-based, fail otherwise.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Thrown if the current OS is not Linux-based.</exception>
        public static void EnsureLinux()
        {
            if (!IsLinux())
            {
                throw new PlatformNotSupportedException();
            }
        }

        /// <summary>
        /// Ensure the current Operating System is POSIX-compliant, fail otherwise.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Thrown if the current OS is not POSIX-compliant.</exception>
        public static void EnsurePosix()
        {
            if (!IsPosix())
            {
                throw new PlatformNotSupportedException();
            }
        }

        public static bool IsElevatedUser()
        {
            if (IsWindows())
            {
#if NETFRAMEWORK
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
#endif
            }
            else if (IsPosix())
            {
                return Unistd.geteuid() == 0;
            }

            return false;
        }

        #region Platform argv[0] Utils

        public static string GetNativeArgv0()
        {
            try
            {
                if (IsWindows())
                {
                    return GetWindowsArgv0();
                }

                if (IsMacOS())
                {
                    return GetMacOSArgv0();
                }

                if (IsLinux())
                {
                    return GetLinuxArgv0();
                }
            }
            catch
            {
                // If there are any issues getting the native argv[0]
                // we should not throw, and certainly not crash!
                // Just return null instead.
            }

            return null;
        }

        private static string GetLinuxArgv0()
        {
            string cmdline = File.ReadAllText("/proc/self/cmdline");
            return cmdline.Split('\0')[0];
        }

        private static string GetMacOSArgv0()
        {
            IntPtr ptr = Interop.MacOS.Native.LibC._NSGetArgv();
            IntPtr argvPtr = Marshal.ReadIntPtr(ptr);
            IntPtr argv0Ptr = Marshal.ReadIntPtr(argvPtr);
            return Marshal.PtrToStringAnsi(argv0Ptr);
        }

        private static string GetWindowsArgv0()
        {
            IntPtr argvPtr = Interop.Windows.Native.Shell32.CommandLineToArgvW(
                Interop.Windows.Native.Kernel32.GetCommandLine(), out _);
            IntPtr argv0Ptr = Marshal.ReadIntPtr(argvPtr);
            string argv0 = Marshal.PtrToStringAuto(argv0Ptr);
            Interop.Windows.Native.Kernel32.LocalFree(argvPtr);
            return argv0;
        }

        #endregion

        #region Platform information helper methods

        private static string GetOSType()
        {
            if (IsWindows())
            {
                return "Windows";
            }

            if (IsMacOS())
            {
                return "macOS";
            }

            if (IsLinux())
            {
                return "Linux";
            }

            return "Unknown";
        }

        private static string GetOSVersion()
        {
            if (IsWindows() && RtlGetVersionEx(out RTL_OSVERSIONINFOEX osvi) == 0)
            {
                return $"{osvi.dwMajorVersion}.{osvi.dwMinorVersion} (build {osvi.dwBuildNumber})";
            }

            if (IsMacOS())
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/sw_vers",
                    Arguments = "-productVersion",
                    RedirectStandardOutput = true
                };

                using (var swvers = new Process { StartInfo = psi })
                {
                    swvers.Start();
                    swvers.WaitForExit();
                    if (swvers.ExitCode == 0)
                    {
                        return swvers.StandardOutput.ReadToEnd().Trim();
                    }
                }
            }

            if (IsLinux())
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "uname",
                    Arguments = "-a",
                    RedirectStandardOutput = true
                };

                using (var uname = new Process { StartInfo = psi })
                {
                    uname.Start();
                    uname.WaitForExit();
                    if (uname.ExitCode == 0)
                    {
                        return uname.StandardOutput.ReadToEnd().Trim();
                    }
                }
            }

            return "Unknown";
        }

        private static string GetCpuArchitecture()
        {
#if NETFRAMEWORK
            return Environment.Is64BitOperatingSystem ? "x86-64" : "x86";
#elif NETSTANDARD
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.Arm:
                    return "ARM32";
                case Architecture.Arm64:
                    return "ARM64";
                case Architecture.X64:
                    return "x86-64";
                case Architecture.X86:
                    return "x86";
                default:
                    return RuntimeInformation.OSArchitecture.ToString();
            }
#endif
        }

        private static string GetClrVersion()
        {
#if NETFRAMEWORK
            return $".NET Framework {Environment.Version}";
#elif NETSTANDARD
            return RuntimeInformation.FrameworkDescription;
#endif
        }

        #endregion

        #region Windows Native Version APIs

        // Interop code sourced from the .NET Runtime as of version 5.0:
        // https://github.com/dotnet/runtime/blob/6578f257e3be2e2144a65769706e981961f0130c/src/libraries/Common/src/Interop/Windows/NtDll/Interop.RtlGetVersion.cs

        [DllImport("ntdll.dll", ExactSpelling = true)]
        private static extern int RtlGetVersion(ref RTL_OSVERSIONINFOEX lpVersionInformation);

        private static unsafe int RtlGetVersionEx(out RTL_OSVERSIONINFOEX osvi)
        {
            osvi = default;
            osvi.dwOSVersionInfoSize = (uint)sizeof(RTL_OSVERSIONINFOEX);
            return RtlGetVersion(ref osvi);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private unsafe struct RTL_OSVERSIONINFOEX
        {
            internal uint dwOSVersionInfoSize;
            internal uint dwMajorVersion;
            internal uint dwMinorVersion;
            internal uint dwBuildNumber;
            internal uint dwPlatformId;
            internal fixed char szCSDVersion[128];
            internal ushort wServicePackMajor;
            internal ushort wServicePackMinor;
            internal short wSuiteMask;
            internal byte wProductType;
            internal byte wReserved;
        }

        /// <summary>
        /// The operating system is Windows client.
        /// </summary>
        private const byte VER_NT_WORKSTATION = 0x0000001;

        /// <summary>
        /// The system is a domain controller and the operating system is Windows Server.
        /// </summary>
        private const byte VER_NT_DOMAIN_CONTROLLER = 0x0000002;

        /// <summary>
        /// The operating system is Windows Server.
        /// </summary>
        /// <remarks>
        /// A server that is also a domain controller is reported as VER_NT_DOMAIN_CONTROLLER, not VER_NT_SERVER.
        /// </remarks>
        private const byte VER_NT_SERVER = 0x0000003;

        #endregion
    }

    public struct PlatformInformation
    {
        public PlatformInformation(string osType, string osVersion, string cpuArch, string clrVersion)
        {
            OperatingSystemType = osType;
            OperatingSystemVersion = osVersion;
            CpuArchitecture = cpuArch;
            ClrVersion = clrVersion;
        }

        public readonly string OperatingSystemType;
        public readonly string OperatingSystemVersion;
        public readonly string CpuArchitecture;
        public readonly string ClrVersion;
    }
}
