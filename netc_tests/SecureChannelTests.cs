using System.Buffers.Binary;
using NetcShared;
using Xunit;

namespace NetcTests;

public class SecureChannelTests
{
    [Fact]
    public async Task RoundTrip_PreservesTypeAndPayload()
    {
        byte[] key = NetcCrypto.DeriveKey("secret");
        using var ms = new MemoryStream();

        using (var send = new SecureChannel(ms, key))
            await send.SendAsync(MsgType.Hello, new byte[] { 1, 2, 3 });

        ms.Position = 0;
        using var recv = new SecureChannel(ms, key);
        var msg = await recv.ReceiveAsync();

        Assert.NotNull(msg);
        Assert.Equal(MsgType.Hello, msg!.Value.Type);
        Assert.Equal(new byte[] { 1, 2, 3 }, msg.Value.Payload);
    }

    [Fact]
    public async Task EmptyPayload_IsAllowed()
    {
        byte[] key = NetcCrypto.DeriveKey("secret");
        using var ms = new MemoryStream();

        using (var send = new SecureChannel(ms, key))
            await send.SendAsync(MsgType.UnlockScreen, Array.Empty<byte>());

        ms.Position = 0;
        using var recv = new SecureChannel(ms, key);
        var msg = await recv.ReceiveAsync();

        Assert.NotNull(msg);
        Assert.Empty(msg!.Value.Payload);
    }

    [Fact]
    public async Task WrongKey_ReturnsNull()
    {
        using var ms = new MemoryStream();
        using (var send = new SecureChannel(ms, NetcCrypto.DeriveKey("key-a")))
            await send.SendAsync(MsgType.Hello, new byte[] { 9 });

        ms.Position = 0;
        using var recv = new SecureChannel(ms, NetcCrypto.DeriveKey("key-b"));
        Assert.Null(await recv.ReceiveAsync());
    }

    [Fact]
    public async Task FrameWithoutPayload_ReturnsNull_NoOverflow()
    {
        // blobLen == NonceSize + TagSize (28) — кадр без байта типа; раньше давал new byte[-1].
        byte[] len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, 12 + 16);
        using var ms = new MemoryStream(len);
        using var recv = new SecureChannel(ms, NetcCrypto.DeriveKey("k"));
        Assert.Null(await recv.ReceiveAsync());
    }

    [Fact]
    public async Task OversizedFrame_ReturnsNull()
    {
        byte[] len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, 64 * 1024 * 1024);
        using var ms = new MemoryStream(len);
        using var recv = new SecureChannel(ms, NetcCrypto.DeriveKey("k"));
        Assert.Null(await recv.ReceiveAsync());
    }

    [Fact]
    public void DeriveKey_IsDeterministicAnd32Bytes()
    {
        Assert.Equal(NetcCrypto.DeriveKey("x"), NetcCrypto.DeriveKey("x"));
        Assert.Equal(32, NetcCrypto.DeriveKey("x").Length);
        Assert.NotEqual(NetcCrypto.DeriveKey("x"), NetcCrypto.DeriveKey("y"));
    }
}
