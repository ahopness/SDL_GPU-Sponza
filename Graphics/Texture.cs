using System.Runtime.InteropServices;
using SDL3;

namespace SponzaTest.Graphics;

public class CpuTexture
{
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public byte[]? Rgba8Data { get; set; }
    public float[]? Rgba32FloatData { get; set; }
    public bool FlipY { get; set; }
}

public sealed class GpuTexture : IDisposable
{
    private readonly SdlGpuContext _ctx;
    public IntPtr Handle { get; private set; }
    public IntPtr Sampler { get; private set; }
    public int Width { get; }
    public int Height { get; }
    public int DepthOrArrayLayers { get; }
    public GpuTexture(SdlGpuContext ctx, int width, int height, SDL.GPUTextureFormat format, SDL.GPUTextureUsageFlags usage,
        int depthOrArrayLayers = 1, uint mipLevelCount = 1, SDL.GPUFilter filter = SDL.GPUFilter.Nearest,
        SDL.GPUSamplerAddressMode addressMode = SDL.GPUSamplerAddressMode.ClampToEdge, string label = "", bool array = false)
    {
        _ctx = ctx; Width = width; Height = height; DepthOrArrayLayers = depthOrArrayLayers;
        var info = new SDL.GPUTextureCreateInfo { Type = array ? SDL.GPUTextureType.TextureType2DArray : SDL.GPUTextureType.TextureType2D,
            Format = format, Usage = usage, Width = (uint)width, Height = (uint)height,
            LayerCountOrDepth = (uint)depthOrArrayLayers, NumLevels = mipLevelCount, SampleCount = SDL.GPUSampleCount.SampleCount1 };
        Handle = SdlGpuContext.Check(SDL.CreateGPUTexture(ctx.Device, in info));
        var sampler = new SDL.GPUSamplerCreateInfo { MinFilter = filter, MagFilter = filter,
            MipmapMode = SDL.GPUSamplerMipmapMode.Linear, AddressModeU = addressMode, AddressModeV = addressMode,
            AddressModeW = addressMode, MaxLod = mipLevelCount - 1 };
        try { Sampler = SdlGpuContext.Check(SDL.CreateGPUSampler(ctx.Device, in sampler)); } catch { Dispose(); throw; }
    }
    public void UploadLayer<T>(T[] data, int bytesPerPixel, uint layer = 0, uint mip = 0, int width = 0, int height = 0) where T : unmanaged
    {
        int w = width > 0 ? width : Math.Max(1, Width >> (int)mip), h = height > 0 ? height : Math.Max(1, Height >> (int)mip);
        var bytes = MemoryMarshal.AsBytes(data.AsSpan());
        if (bytes.Length != checked(w * h * bytesPerPixel)) throw new ArgumentException("Texture data size does not match the mip dimensions.");
        _ctx.Upload(bytes, (pass, transfer) => {
            var source = new SDL.GPUTextureTransferInfo { TransferBuffer = transfer, PixelsPerRow = (uint)w, RowsPerLayer = (uint)h };
            var dest = new SDL.GPUTextureRegion { Texture = Handle, MipLevel = mip, Layer = layer, W = (uint)w, H = (uint)h, D = 1 };
            SDL.UploadToGPUTexture(pass, in source, in dest, false);
        });
    }
    public SDL.GPUTextureSamplerBinding Binding => new() { Texture = Handle, Sampler = Sampler };
    public void Dispose() { if (Sampler != IntPtr.Zero) SDL.ReleaseGPUSampler(_ctx.Device, Sampler); if (Handle != IntPtr.Zero) SDL.ReleaseGPUTexture(_ctx.Device, Handle); Sampler = Handle = IntPtr.Zero; }
}