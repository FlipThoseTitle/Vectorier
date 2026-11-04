using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Vectorier.Services.Collab;

// ================= MESSAGES ================= //
// Every message is a small JSON object ("t" says which one) followed by optional raw bytes (level snapshots, file
// chunks). Participants talk only to the host; the host checks each request and forwards what others need.

[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(HelloMsg), "hello")]
[JsonDerivedType(typeof(PendingMsg), "pending")]
[JsonDerivedType(typeof(AcceptedMsg), "accepted")]
[JsonDerivedType(typeof(ClosedMsg), "closed")]
[JsonDerivedType(typeof(LeaveMsg), "leave")]
[JsonDerivedType(typeof(UsersMsg), "users")]
[JsonDerivedType(typeof(PingMsg), "ping")]
[JsonDerivedType(typeof(ManifestMsg), "manifest")]
[JsonDerivedType(typeof(FileRequestMsg), "fileRequest")]
[JsonDerivedType(typeof(FileChunkMsg), "fileChunk")]
[JsonDerivedType(typeof(SyncedMsg), "synced")]
[JsonDerivedType(typeof(LevelMsg), "level")]
[JsonDerivedType(typeof(OpsMsg), "ops")]
[JsonDerivedType(typeof(PresenceMsg), "presence")]
[JsonDerivedType(typeof(FilesChangedMsg), "filesChanged")]
[JsonDerivedType(typeof(ChangeRequestMsg), "changeRequest")]
[JsonDerivedType(typeof(ChangeDecisionMsg), "changeDecision")]
[JsonDerivedType(typeof(NoticeMsg), "notice")]
public abstract record CollabMsg;

/// <summary>Participant → host, first message: the secret from the Session ID proves they were given it.</summary>
public sealed record HelloMsg(int Protocol, string Secret, string UserName, string? ProjectKey) : CollabMsg;

/// <summary>Host → participant: waiting for the host to accept.</summary>
public sealed record PendingMsg : CollabMsg;

/// <summary>Host → participant: accepted. Which project and level are shared, and who the participant is.</summary>
public sealed record AcceptedMsg(int YourId, string ProjectName, string ProjectKey, string LevelName, string LevelPath) : CollabMsg;

/// <summary>Either way: the connection ends. Reason: declined, stopped, kicked, auth, busy, version, left.</summary>
public sealed record ClosedMsg(string Reason) : CollabMsg;

public sealed record LeaveMsg : CollabMsg;

public sealed record CollabUser(int Id, string Name, uint Color, bool IsHost);

public sealed record UsersMsg(List<CollabUser> Users) : CollabMsg;

public sealed record PingMsg : CollabMsg;

/// <summary>A project file: path relative to the project folder ('/' separated), size and SHA-256 (hex).</summary>
public sealed record ManifestEntry(string Path, long Size, string Hash);

/// <summary>Host → participant: every file of the shared project (the .vectorier cache folder is never included).</summary>
public sealed record ManifestMsg(List<ManifestEntry> Files) : CollabMsg;

/// <summary>Participant → host: send me these files (only ones in the manifest are served).</summary>
public sealed record FileRequestMsg(List<string> Paths) : CollabMsg;

/// <summary>A piece of a file (bytes follow). Transfer: which batch it belongs to (a download or a change request).</summary>
public sealed record FileChunkMsg(string Transfer, string Path, long Offset, long Size, string Hash) : CollabMsg;

/// <summary>Participant → host: the project matches; ready for the level.</summary>
public sealed record SyncedMsg : CollabMsg;

/// <summary>Host → participant: the whole shared level (bytes follow), and the order number it is at.</summary>
public sealed record LevelMsg(long Sequence) : CollabMsg;

/// <summary>
/// Level edits. From a participant: its own changes (Batch numbers its batches). From the host: changes in their final
/// order (Sequence), with who made them (Author) and, for the author, which of its batches this was.
/// </summary>
public sealed record OpsMsg(List<LevelOp> Ops, long Sequence, int Author, long Batch) : CollabMsg;

/// <summary>Where someone is looking, what they have selected, and which hierarchies their Dynamic Editor holds.</summary>
public sealed record PresenceMsg(int User, double X, double Y, List<long> Selection, List<long> DynamicRoots) : CollabMsg;

/// <summary>Host → participants: project files changed (they download the changed ones, delete the removed ones).</summary>
public sealed record FilesChangedMsg(List<ManifestEntry> Changed, List<string> Removed) : CollabMsg;

/// <summary>
/// Participant → host: asks to add / replace / remove project files (the added files follow as chunks with the same
/// Id). Host → participant: the same message tells the participant which request is meant when it is decided.
/// </summary>
public sealed record ChangeRequestMsg(string Id, List<ManifestEntry> Changed, List<string> Removed) : CollabMsg;

public sealed record ChangeDecisionMsg(string Id, bool Approved, string? Reason) : CollabMsg;

/// <summary>A line of information shown to the user (sync failures, refused requests...).</summary>
public sealed record NoticeMsg(string Text) : CollabMsg;

// ================= LEVEL OPERATIONS ================= //

/// <summary>
/// One change to the shared level, about one element identified by its collaboration id:
/// "set" creates or updates it (Xml = the element without its children, Parent / Index = where it sits),
/// "remove" deletes it with everything inside, "settings" replaces the level settings (Xml).
/// </summary>
public sealed record LevelOp(string Kind, long Id, long Parent, int Index, string? Xml);

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CollabMsg))]
[JsonSerializable(typeof(List<LevelOp>))]
internal partial class CollabJsonContext : JsonSerializerContext
{
}

// ================= FRAMING ================= //

/// <summary>Length-prefixed frames: [JSON length][bytes length][JSON][bytes], with size limits against abuse.</summary>
public static class CollabWire
{
    // 2: elements carry their Layer and Order in Layer; Dynamic Editor playback is no longer sent.
    public const int Protocol = 2;
    public const int MaxJson = 4 * 1024 * 1024;
    public const int MaxBytes = 32 * 1024 * 1024;

    public static byte[] Pack(CollabMsg message, ReadOnlySpan<byte> bytes)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, CollabJsonContext.Default.CollabMsg);
        if (json.Length > MaxJson || bytes.Length > MaxBytes)
            throw new InvalidOperationException("Collaboration message too large.");
        var frame = new byte[8 + json.Length + bytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, json.Length);
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4), bytes.Length);
        json.CopyTo(frame, 8);
        bytes.CopyTo(frame.AsSpan(8 + json.Length));
        return frame;
    }

    /// <summary>Reads one frame; null at the end of the stream.</summary>
    public static async Task<(CollabMsg Message, byte[] Bytes)?> ReadAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[8];
        if (!await ReadExactAsync(stream, header, token))
            return null;
        int jsonLength = BinaryPrimitives.ReadInt32BigEndian(header);
        int byteLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4));
        if (jsonLength <= 0 || jsonLength > MaxJson || byteLength < 0 || byteLength > MaxBytes)
            throw new InvalidDataException("Invalid collaboration frame.");
        var json = new byte[jsonLength];
        if (!await ReadExactAsync(stream, json, token))
            return null;
        var bytes = byteLength == 0 ? Array.Empty<byte>() : new byte[byteLength];
        if (byteLength > 0 && !await ReadExactAsync(stream, bytes, token))
            return null;
        var message = JsonSerializer.Deserialize(json, CollabJsonContext.Default.CollabMsg)
            ?? throw new InvalidDataException("Empty collaboration message.");
        return (message, bytes);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), token);
            if (n == 0)
                return false;
            read += n;
        }
        return true;
    }
}
