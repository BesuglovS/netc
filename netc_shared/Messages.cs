using System.Buffers.Binary;

namespace NetcShared;

/// <summary>Типы сообщений локального канала (служба ↔ трей-агент).</summary>
public enum PipeMsg : byte
{
    Register = 1,
    Capture = 2,
    Frame = 3,
    Input = 4,
    Ping = 5,
    ShowBroadcast = 10,
    BroadcastFrame = 11,
    HideBroadcast = 12,
    Lock = 13,
    Unlock = 14
}

/// <summary>Параметры блокировки экрана.</summary>
public sealed class LockMessage
{
    public bool Blank { get; set; }
    public string Text { get; set; } = "Экран заблокирован учителем";
}

/// <summary>Параметры запроса кадра экрана.</summary>
public sealed class CaptureRequest
{
    public int MaxWidth { get; set; } = 320;
    public int Quality { get; set; } = 40;
}

/// <summary>Параметры начала потока экрана (учитель → служба).</summary>
public sealed class ScreenStartMessage
{
    public int MaxWidth { get; set; } = 320;
    public int Quality { get; set; } = 40;
    public int Fps { get; set; } = 2;
}

/// <summary>Событие ввода (мышь/клавиатура).</summary>
public sealed class InputEvent
{
    public string Type { get; set; } = "mouse";   // mouse | key
    public string Action { get; set; } = "move";   // move | down | up | wheel
    public string Button { get; set; } = "left";   // left | right | middle
    public int X { get; set; }
    public int Y { get; set; }
    public int Delta { get; set; }
    public int Vk { get; set; }
    public bool Down { get; set; }
}

/// <summary>Кадрирование без шифрования (для локального именованного канала): [len:4][type:1][payload].</summary>
public static class NetFrame
{
    private const int MaxFrame = 64 * 1024 * 1024;

    public static async Task WriteAsync(Stream stream, byte type, byte[] payload, CancellationToken ct = default)
    {
        payload ??= Array.Empty<byte>();
        byte[] header = new byte[5];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), payload.Length);
        header[4] = type;
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        if (payload.Length > 0)
        {
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        }
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<(byte Type, byte[] Payload)?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[5];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false)) return null;

        int len = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4));
        if (len < 0 || len > MaxFrame) return null;

        byte[] payload = new byte[len];
        if (len > 0 && !await ReadExactAsync(stream, payload, ct).ConfigureAwait(false)) return null;
        return (header[4], payload);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int off = 0;
        while (off < buffer.Length)
        {
            int r = await stream.ReadAsync(buffer.AsMemory(off), ct).ConfigureAwait(false);
            if (r == 0) return false;
            off += r;
        }
        return true;
    }
}
