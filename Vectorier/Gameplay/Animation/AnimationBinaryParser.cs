#nullable disable warnings
using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Numerics;
using Vectorier.Gameplay.Frame;

namespace Vectorier.Gameplay.Animation;

public static class AnimationBinaryParser
{
    // Concurrent: every animation is parsed in parallel at start-up.
    private static readonly ConcurrentDictionary<string, Provider> CachedBinary = new ConcurrentDictionary<string, Provider>();

    public static void ClearCachedBinary()
    {
        CachedBinary.Clear();
    }

    public static Provider ParseFile(string fileName, bool useCache = true)
    {
        if (useCache)
        {
            if (CachedBinary.TryGetValue(fileName, out var cached))
            {
                return cached;
            }
        }
        var bytes = ResourceManager.GetBinary(fileName);
        if (bytes == null)
        {
            Diagnostics.GameLog.Warning("Loading the moves", "The move's animation file does not exist: the move has no frames.", fileName);
            return new Provider(0);
        }
        Provider provider;
        try
        {
            provider = Parse(bytes);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException or OutOfMemoryException)
        {
            throw new Diagnostics.GameDataException(
                $"The animation file is cut short or is not a move animation (.bin, {bytes.Length} bytes).", fileName, inner: ex);
        }
        if (useCache)
        {
            CachedBinary.TryAdd(fileName, provider);
        }
        return provider;
    }

    /// <summary>An animation .bin read beforehand (frames of nodes, Y turned down like the models).</summary>
    public static Provider Parse(byte[] bytes)
    {
        // Read straight from the bytes (little-endian, like BinaryReader) without a stream per value.
        var span = new ReadOnlySpan<byte>(bytes);
        int pos = 0;
        int count = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos)); pos += 4;
        var provider = new Provider(count);

        for (int i = 0; i < count; i++)
        {
            pos += 1; // frame flag
            int num = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(pos)); pos += 4;
            Vector3[] array = new Vector3[num];

            for (int j = 0; j < num; j++)
            {
                array[j].X = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(pos));
                array[j].Y = -BinaryPrimitives.ReadSingleLittleEndian(span.Slice(pos + 4));
                array[j].Z = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(pos + 8));
                pos += 12;
            }
            provider.Add(array, i);
        }
        return provider;
    }
}
