using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Vectorier.Services.Collab;

/// <summary>
/// The Session ID people share to join: everything needed to reach the host safely, in one copyable code.
/// It holds the host's address and port, the fingerprint of the host's encryption certificate (so the joiner talks to
/// the real host and nobody in between), and a random secret (so only people given the code can ask to join).
/// Written in Crockford Base32 groups of four: no confusable letters, case and dashes do not matter.
/// </summary>
public sealed record CollabCode(string Host, int Port, byte[] Fingerprint, byte[] Secret)
{
    private const byte Version = 1;
    public const int FingerprintLength = 16;
    public const int SecretLength = 16;
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public override string ToString()
    {
        var bytes = new List<byte> { Version };
        if (IPAddress.TryParse(Host, out var ip))
        {
            var raw = ip.GetAddressBytes();
            bytes.Add(raw.Length == 4 ? (byte)4 : (byte)6);
            bytes.AddRange(raw);
        }
        else
        {
            var name = Encoding.UTF8.GetBytes(Host);
            if (name.Length is 0 or > 200)
                throw new ArgumentException("The host name is too long.");
            bytes.Add(0);
            bytes.Add((byte)name.Length);
            bytes.AddRange(name);
        }
        bytes.Add((byte)(Port >> 8));
        bytes.Add((byte)Port);
        bytes.AddRange(Fingerprint);
        bytes.AddRange(Secret);
        string text = Encode(bytes.ToArray());
        return string.Join("-", Enumerable.Range(0, (text.Length + 3) / 4).Select(i => text.Substring(i * 4, Math.Min(4, text.Length - i * 4))));
    }

    /// <summary>Reads a pasted code (spaces, dashes, case and O/I/L look-alikes are tolerated). Null when it is not one.</summary>
    public static CollabCode? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var bytes = Decode(text);
        if (bytes == null || bytes.Length < 2 || bytes[0] != Version)
            return null;
        try
        {
            int pos = 1;
            string host;
            byte kind = bytes[pos++];
            if (kind == 4 || kind == 6)
            {
                int len = kind == 4 ? 4 : 16;
                host = new IPAddress(bytes.AsSpan(pos, len)).ToString();
                pos += len;
            }
            else if (kind == 0)
            {
                int len = bytes[pos++];
                host = Encoding.UTF8.GetString(bytes, pos, len);
                pos += len;
            }
            else
            {
                return null;
            }
            int port = (bytes[pos] << 8) | bytes[pos + 1];
            pos += 2;
            if (port == 0 || bytes.Length - pos < FingerprintLength + SecretLength)
                return null;
            var fingerprint = bytes.AsSpan(pos, FingerprintLength).ToArray();
            pos += FingerprintLength;
            var secret = bytes.AsSpan(pos, SecretLength).ToArray();
            return new CollabCode(host, port, fingerprint, secret);
        }
        catch
        {
            return null;
        }
    }

    private static string Encode(byte[] data)
    {
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[]? Decode(string text)
    {
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (char raw in text.ToUpperInvariant())
        {
            char c = raw switch { 'O' => '0', 'I' or 'L' => '1', _ => raw };
            if (c is '-' or ' ' or '\t' or '\r' or '\n')
                continue;
            int value = Alphabet.IndexOf(c);
            if (value < 0)
                return null;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return output.ToArray();
    }
}
