using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ProjectXLR.Hardware;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct TUsbDeviceProperties
{
    public int VendorId;
    public int ProductId;
    public int RevisionNumber;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string SerialNumber;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Manufacturer;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Model;

    public int UnknownNumber;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string UnknownString;
}

internal static class TUsbAudioInterop
{
    private const string DllPathX64 = @"C:\Program Files\TC-Helicon\GoXLR_Audio_Driver\W10_x64\goxlr_audioapi_x64.dll";
    private const string DllName = "goxlr_audioapi_x64.dll";

    static TUsbAudioInterop()
    {
        NativeLibrary.SetDllImportResolver(typeof(TUsbAudioInterop).Assembly, (libraryName, _, _) =>
        {
            if (string.Equals(libraryName, DllName, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(DllPathX64))
                {
                    return NativeLibrary.Load(DllPathX64);
                }
            }
            return IntPtr.Zero;
        });
    }

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint TUSBAUDIO_EnumerateDevices();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint TUSBAUDIO_GetDeviceCount();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint TUSBAUDIO_OpenDeviceByIndex(uint deviceIndex, out uint deviceHandle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint TUSBAUDIO_CloseDevice(uint deviceHandle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    public static extern uint TUSBAUDIO_GetDeviceProperties(uint deviceHandle, out TUsbDeviceProperties properties);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern IntPtr TUSBAUDIO_StatusCodeStringA(uint statusCode);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern uint TUSBAUDIO_RegisterDeviceNotification(
        uint deviceHandle,
        uint eventCategoryFilter,
        IntPtr eventHandle,
        uint flags);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern unsafe uint TUSBAUDIO_ReadDeviceNotification(
        uint deviceHandle,
        ref uint eventCategory,
        byte* dataBuffer,
        uint dataBufferSize,
        out uint bytesReturned);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern unsafe uint TUSBAUDIO_ClassVendorRequestOut(
        uint deviceHandle,
        uint recipient,
        uint requestType,
        uint request,
        uint value,
        ushort index,
        ushort dataLength,
        byte* dataBuffer,
        byte* bytesTransferredBuffer,
        uint bytesTransferredBufferSize);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern unsafe uint TUSBAUDIO_ClassVendorRequestIn(
        uint deviceHandle,
        uint recipient,
        uint requestType,
        uint request,
        uint value,
        ushort index,
        ushort maxDataLength,
        byte* dataBuffer,
        byte* bytesTransferredBuffer,
        uint bytesTransferredBufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEventW(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    public static string GetStatusText(uint status)
    {
        try
        {
            IntPtr ptr = TUSBAUDIO_StatusCodeStringA(status);
            if (ptr != IntPtr.Zero)
            {
                return Marshal.PtrToStringAnsi(ptr) ?? $"0x{status:X8}";
            }
        }
        catch
        {
            // Ignore
        }
        return $"0x{status:X8}";
    }
}
