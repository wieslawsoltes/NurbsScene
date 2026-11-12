using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace NurbsScene.Rendering;

/// <summary>Canonical offline integration shared with the reusable JavaScript renderer.</summary>
internal sealed class StudioEnvironmentData
{
    internal const string PayloadSha256 = "8a72c9015e07baaaaec137b69322f4f48e5df448d2a24740c6bef55df0c03c21";
    private const int PayloadBytes = 1179808;
    private static readonly Lazy<StudioEnvironmentData> Data = new(Load);
    public static StudioEnvironmentData Shared => Data.Value;
    public int CubeSize => 128;
    public int MipCount => 8;
    public int LutSize => 128;
    public ReadOnlyMemory<byte> SphericalHarmonics { get; }
    public IReadOnlyList<StudioEnvironmentMip> Mips { get; }
    public ReadOnlyMemory<byte> BrdfLut { get; }

    private StudioEnvironmentData(byte[] bytes)
    {
        SphericalHarmonics = bytes.AsMemory(32, 144);
        var mips = new StudioEnvironmentMip[MipCount];
        var offset = 176;
        for (var level = 0; level < MipCount; level++)
        {
            var size = CubeSize >> level;
            var length = checked(size * size * 6 * 8);
            mips[level] = new(size, bytes.AsMemory(offset, length));
            offset += length;
        }
        Mips = Array.AsReadOnly(mips);
        BrdfLut = bytes.AsMemory(offset, LutSize * LutSize * 8);
    }

    private static StudioEnvironmentData Load()
    {
        using var resource = typeof(StudioEnvironmentData).Assembly.GetManifestResourceStream(
            "NurbsScene.Rendering.Lighting.studio.ibl.gz")
            ?? throw new InvalidOperationException("The bundled WebGPU studio environment is missing.");
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        var bytes = new byte[PayloadBytes];
        gzip.ReadExactly(bytes);
        if (gzip.ReadByte() != -1 || !bytes.AsSpan(0, 8).SequenceEqual("NSIBL001"u8)
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8)) != 128
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12)) != 8
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)) != 128
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(20)) != 9
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(24)) != 0
            || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(28)) != 0
            || !Convert.ToHexStringLower(SHA256.HashData(bytes)).Equals(PayloadSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The bundled WebGPU studio environment has invalid provenance or layout.");
        return new(bytes);
    }
}

internal sealed record StudioEnvironmentMip(int Size, ReadOnlyMemory<byte> Pixels);
