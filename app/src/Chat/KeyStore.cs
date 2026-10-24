using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Godot;

namespace DigitalDoll.Chat;

/// <summary>
/// API keys, kept apart from the other settings. On Windows each key is encrypted
/// with DPAPI for the current Windows user, so the file is useless on another
/// account or computer. Other systems (development builds) store them unencrypted.
/// </summary>
public static class KeyStore
{
    private const string Path = "user://keys.json";
    private const string PlainPrefix = "plain:";

    public static string? Get(string providerId)
    {
        var keys = Load();
        if (!keys.TryGetValue(providerId, out var stored) || string.IsNullOrEmpty(stored))
            return null;
        try
        {
            return stored.StartsWith(PlainPrefix, StringComparison.Ordinal)
                ? stored[PlainPrefix.Length..]
                : Encoding.UTF8.GetString(Unprotect(Convert.FromBase64String(stored)));
        }
        catch (Exception e)
        {
            GD.PushWarning($"Could not read the API key: {e.Message}");
            return null;
        }
    }

    public static void Set(string providerId, string? key)
    {
        if (SelfTest.Enabled)
            return;
        var keys = Load();
        if (string.IsNullOrWhiteSpace(key))
            keys.Remove(providerId);
        else
            keys[providerId] = OperatingSystem.IsWindows()
                ? Convert.ToBase64String(Protect(Encoding.UTF8.GetBytes(key.Trim())))
                : PlainPrefix + key.Trim();
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        file?.StoreString(JsonSerializer.Serialize(keys));
    }

    private static Dictionary<string, string> Load()
    {
        if (!FileAccess.FileExists(Path))
            return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(FileAccess.GetFileAsString(Path)) ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public IntPtr Data;
    }

    private const int UiForbidden = 0x1;

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, ref DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    private static byte[] Protect(byte[] data) => Run(data, protect: true);

    private static byte[] Unprotect(byte[] data)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("encrypted key from Windows");
        return Run(data, protect: false);
    }

    private static byte[] Run(byte[] data, bool protect)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var input = new DataBlob { Size = data.Length, Data = handle.AddrOfPinnedObject() };
            var output = new DataBlob();
            bool ok = protect
                ? CryptProtectData(ref input, "AI Digital Doll", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, UiForbidden, ref output);
            if (!ok)
                throw new InvalidOperationException($"DPAPI error {Marshal.GetLastWin32Error()}");
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, output.Size);
            LocalFree(output.Data);
            return result;
        }
        finally
        {
            handle.Free();
        }
    }
}
