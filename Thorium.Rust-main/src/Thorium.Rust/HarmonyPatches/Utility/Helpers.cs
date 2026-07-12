using System;
using System.IO;
using System.Text;

namespace Thorium.Rust.HarmonyPatches.Utility;

public static class Helpers
{
    public static long TryExtractNetId(BaseNetworkable? networkable)
    {
        if (networkable is not { net.ID.Value: var value }) return 0;
        return (long)value;
    }

    public static long GetSteamIdOrZero(BasePlayer? player)
    {
        if (player is not { userID._value: var userID }) return 0;
        return (long)userID;
    }

    public static ulong GetSteamIdUlongOrZero(BasePlayer? player)
    {
        return player is not { userID._value: var userID } ? 0 : userID;
    }

    //Write an int to a stream
    public static void Write(MemoryStream stream, uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    //Write a ulong to a stream
    public static void Write(MemoryStream stream, ulong value)
    {
        var bytes = BitConverter.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    //Write a byte to a stream
    public static void Write(MemoryStream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Write(stream, (uint)bytes.Length);
        stream.Write(bytes, 0, bytes.Length);
    }

    //Write a byte array to a stream
    public static void Write(MemoryStream stream, byte[] value)
    {
        Write(stream, (uint)value.Length);
        stream.Write(value, 0, value.Length);
    }

    //Write a float to a stream
    public static void Write(MemoryStream stream, float value)
    {
        var bytes = BitConverter.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }

    public static void WriteCappedBytes(Stream stream, byte[] value, int length)
    {
        if (value.Length > length)
        {
            throw new ArgumentException($"Byte array length exceeds the specified length of {length}.");
        }

        // Write the length of the byte array
        stream.Write(BitConverter.GetBytes(value.Length), 0, sizeof(int));

        // Write the byte array
        stream.Write(value, 0, value.Length);

        // Pad with zeros if necessary (write byte-by-byte to avoid allocating a padding array)
        for (var i = value.Length; i < length; i++)
            stream.WriteByte(0);
    }
}