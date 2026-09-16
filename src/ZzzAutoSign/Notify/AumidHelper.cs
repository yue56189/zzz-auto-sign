using System.Runtime.InteropServices;
using System.Text;
using ZzzAutoSign.Config;
using ZzzAutoSign.Logging;

namespace ZzzAutoSign.Notify;

/// <summary>
/// AUMID（AppUserModelID）辅助。
/// 使用自定义 AUMID 时，Windows 要求在开始菜单存在一个指向本 exe 且带该 AUMID 的快捷方式，
/// 否则 Toast 可能不显示。这里在启动时自动创建该快捷方式。
/// </summary>
public static class AumidHelper
{
    /// <summary>确保开始菜单快捷方式存在。返回是否成功。</summary>
    public static bool EnsureShortcut(ILogger log)
    {
        try
        {
            string startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");

            Directory.CreateDirectory(startMenu);
            string lnkPath = Path.Combine(startMenu, AppPaths.DisplayName + ".lnk");

            // 已存在且指向当前 exe 就不必重建
            if (File.Exists(lnkPath))
            {
                return true;
            }

            string? exe = AppPaths.ExecutablePath;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            {
                log.Warn("找不到自身可执行文件路径，跳过快捷方式创建");
                return false;
            }

            CreateShortcut(lnkPath, exe, AppPaths.DisplayName, AppPaths.AppUserModelId);
            log.Info($"已创建开始菜单快捷方式：{lnkPath}");
            return true;
        }
        catch (Exception ex)
        {
            log.Warn($"创建快捷方式失败（Toast 可能无法显示）：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 通过 IShellLink + IPersistFile 创建 .lnk，并设置 AppUserModelID 属性。
    /// 纯托管实现，无需额外依赖。
    /// </summary>
    private static void CreateShortcut(string lnkPath, string targetExe, string description, string aumid)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(targetExe);
            link.SetDescription(description);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetExe) ?? string.Empty);

            // 关键：写入 AppUserModelID，使 Toast 能归属到本应用
            var store = (IPropertyStore)link;
            var key = new PropertyKey(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5); // PKEY_AppUserModel_ID
            var variant = PropVariant.FromString(aumid);
            try
            {
                store.SetValue(ref key, ref variant);
                store.Commit();
            }
            finally
            {
                variant.Clear();
            }

            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    // ---------------- COM 互操作定义 ----------------

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;

        public PropertyKey(Guid formatId, int propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort VarType;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr PointerValue;
        public int Int32Value;

        public static PropVariant FromString(string value)
        {
            return new PropVariant
            {
                VarType = 31, // VT_LPWSTR
                PointerValue = Marshal.StringToCoTaskMemUni(value)
            };
        }

        /// <summary>释放由本结构分配的字符串内存。</summary>
        public void Clear()
        {
            if (PointerValue != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(PointerValue);
                PointerValue = IntPtr.Zero;
            }
        }
    }
}
