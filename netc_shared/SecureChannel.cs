using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NetcShared;

/// <summary>
/// Защищённый канал поверх потока: кадры длиной 4 байта (big-endian),
/// полезная нагрузка зашифрована AES-GCM. Открытый текст: [1 байт тип][данные].
/// Неверный ключ приводит к ошибке тега — соединение отклоняется.
/// </summary>
public sealed class SecureChannel : IDisposable
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaxFrame = 16 * 1024 * 1024;

    private readonly Stream _stream;
    private readonly AesGcm _aes;

    public SecureChannel(Stream stream, byte[] key, bool ownsStream = false)
    {
        _stream = stream;
        _aes = new AesGcm(key, TagSize);
        OwnsStream = ownsStream;
    }

    public bool OwnsStream { get; }

    public async Task SendAsync(MsgType type, byte[]? payload, CancellationToken ct = default)
    {
        payload ??= Array.Empty<byte>();
        int ptLen = 1 + payload.Length;
        byte[] plain = new byte[ptLen];
        plain[0] = (byte)type;
        if (payload.Length > 0) Buffer.BlockCopy(payload, 0, plain, 1, payload.Length);

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[ptLen];
        byte[] tag = new byte[TagSize];
        _aes.Encrypt(nonce, plain, cipher, tag);

        int blobLen = NonceSize + ptLen + TagSize;
        byte[] frame = new byte[4 + blobLen];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, 4), blobLen);
        Buffer.BlockCopy(nonce, 0, frame, 4, NonceSize);
        Buffer.BlockCopy(cipher, 0, frame, 4 + NonceSize, ptLen);
        Buffer.BlockCopy(tag, 0, frame, 4 + NonceSize + ptLen, TagSize);

        await _stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await _stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public async Task<(MsgType Type, byte[] Payload)?> ReceiveAsync(CancellationToken ct = default)
    {
        byte[] lenBuf = new byte[4];
        if (!await ReadExactAsync(lenBuf, ct).ConfigureAwait(false)) return null;
        int blobLen = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
        // Минимум: nonce + 1 байт типа + тег. Пустая полезная нагрузка недопустима
        // (иначе ниже new byte[ptLen - 1] уйдёт в отрицательный размер).
        if (blobLen <= NonceSize + TagSize || blobLen > MaxFrame) return null;

        byte[] blob = new byte[blobLen];
        if (!await ReadExactAsync(blob, ct).ConfigureAwait(false)) return null;

        int ptLen = blobLen - NonceSize - TagSize;
        byte[] plain = new byte[ptLen];
        try
        {
            _aes.Decrypt(
                blob.AsSpan(0, NonceSize),
                blob.AsSpan(NonceSize, ptLen),
                blob.AsSpan(NonceSize + ptLen, TagSize),
                plain);
        }
        catch (CryptographicException)
        {
            return null; // неверный ключ
        }

        byte[] payload = new byte[ptLen - 1];
        if (payload.Length > 0) Buffer.BlockCopy(plain, 1, payload, 0, payload.Length);
        return ((MsgType)plain[0], payload);
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, CancellationToken ct)
    {
        int off = 0;
        while (off < buffer.Length)
        {
            int r = await _stream.ReadAsync(buffer.AsMemory(off), ct).ConfigureAwait(false);
            if (r == 0) return false;
            off += r;
        }
        return true;
    }

    public void Dispose()
    {
        _aes.Dispose();
        if (OwnsStream) _stream.Dispose();
    }
}
