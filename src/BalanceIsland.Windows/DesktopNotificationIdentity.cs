using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace BalanceIsland.Windows;

internal sealed record DesktopNotificationIdentityResult(bool IsRegistered, string? Error);

internal static class DesktopNotificationIdentity
{
    internal const string AppUserModelId = "Noyorin.BalanceIsland";
    private const string ShortcutName = "Balance Island.lnk";
    private static readonly PropertyKey AppUserModelIdProperty =
        new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    internal static DesktopNotificationIdentityResult TryRegister()
    {
        if (!OperatingSystem.IsWindows())
            return new DesktopNotificationIdentityResult(false, "Windows 通知仅在 Windows 上可用。");

        try
        {
            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppUserModelId));
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
                return new DesktopNotificationIdentityResult(false, "无法确定应用程序路径。");

            var shortcutPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutName);
            if (!ShortcutMatches(shortcutPath, executablePath))
                CreateOrReplaceShortcut(shortcutPath, executablePath);

            return new DesktopNotificationIdentityResult(true, null);
        }
        catch (Exception)
        {
            return new DesktopNotificationIdentityResult(false, "无法注册 Windows 通知身份。");
        }
    }

    private static bool ShortcutMatches(string shortcutPath, string executablePath)
    {
        if (!File.Exists(shortcutPath)) return false;

        object? shellLink = null;
        try
        {
            shellLink = new ShellLink();
            var link = (IShellLinkW)shellLink;
            ((IPersistFile)shellLink).Load(shortcutPath, 0);
            var target = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            var propertyStore = (IPropertyStore)shellLink;
            var appUserModelIdKey = AppUserModelIdProperty;
            propertyStore.GetValue(ref appUserModelIdKey, out var appUserModelId);
            try
            {
                return string.Equals(target.ToString(), executablePath, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(appUserModelId.StringValue, AppUserModelId, StringComparison.Ordinal);
            }
            finally
            {
                appUserModelId.Dispose();
            }
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (shellLink is not null) Marshal.FinalReleaseComObject(shellLink);
        }
    }

    private static void CreateOrReplaceShortcut(string shortcutPath, string executablePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        object? shellLink = null;
        try
        {
            shellLink = new ShellLink();
            var link = (IShellLinkW)shellLink;
            link.SetPath(executablePath);
            link.SetWorkingDirectory(Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory);

            var propertyStore = (IPropertyStore)shellLink;
            var appUserModelId = PropVariant.FromString(AppUserModelId);
            try
            {
                var appUserModelIdKey = AppUserModelIdProperty;
                propertyStore.SetValue(ref appUserModelIdKey, ref appUserModelId);
                propertyStore.Commit();
            }
            finally
            {
                appUserModelId.Dispose();
            }

            ((IPersistFile)shellLink).Save(shortcutPath, true);
        }
        finally
        {
            if (shellLink is not null) Marshal.FinalReleaseComObject(shellLink);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

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
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        void GetIDList(out IntPtr itemIdList);
        void SetIDList(IntPtr itemIdList);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr windowHandle, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint propertyCount);
        void GetAt(uint propertyIndex, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        internal readonly Guid FormatId = formatId;
        internal readonly uint PropertyId = propertyId;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] private ushort _valueType;
        [FieldOffset(8)] private IntPtr _pointerValue;

        internal string? StringValue => _valueType == (ushort)VarEnum.VT_LPWSTR
            ? Marshal.PtrToStringUni(_pointerValue)
            : null;

        internal static PropVariant FromString(string value) => new()
        {
            _valueType = (ushort)VarEnum.VT_LPWSTR,
            _pointerValue = Marshal.StringToCoTaskMemUni(value)
        };

        internal void Dispose()
        {
            if (_valueType != 0) PropVariantClear(ref this);
            _valueType = 0;
            _pointerValue = IntPtr.Zero;
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
}
