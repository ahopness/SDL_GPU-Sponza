using static SDL3.SDL;

namespace SponzaTest.Graphics;

public sealed class SdlGpuContext : IDisposable
{
    public IntPtr Window { get; }
    public IntPtr Device { get; }
    public SdlGpuContext(IntPtr window)
    {
        Window = window;
        Device = Check(CreateGPUDevice(GPUShaderFormat.SPIRV | GPUShaderFormat.DXIL | GPUShaderFormat.MSL, true, null));

        if (!ClaimWindowForGPUDevice(Device, window))
        {
            DestroyGPUDevice(Device); 
            throw new InvalidOperationException(GetError());
        }
    }
    public static IntPtr Check(IntPtr handle) => 
        handle != IntPtr.Zero ? 
            handle : throw new InvalidOperationException(GetError());
    
    internal unsafe void Upload(ReadOnlySpan<byte> bytes, Action<IntPtr, IntPtr> copy)
    {
        var info = new GPUTransferBufferCreateInfo { Usage = GPUTransferBufferUsage.Upload, Size = checked((uint)bytes.Length) };
        var transfer = Check(CreateGPUTransferBuffer(Device, in info));
        try
        {
            var ptr = Check(MapGPUTransferBuffer(Device, transfer, false));
            bytes.CopyTo(new Span<byte>((void*)ptr, bytes.Length));
            UnmapGPUTransferBuffer(Device, transfer);
            var command = Check(AcquireGPUCommandBuffer(Device));
            var pass = Check(BeginGPUCopyPass(command));

            copy(pass, transfer);

            EndGPUCopyPass(pass);
            Check(SubmitGPUCommandBuffer(command));
        }
        finally
        {
            ReleaseGPUTransferBuffer(Device, transfer);
        }
    }
    public static void Check(bool success)
    {
        if (!success) 
            throw new InvalidOperationException(GetError());
    }
    
    public void Dispose()
    {
        WaitForGPUIdle(Device); 
        ReleaseWindowFromGPUDevice(Device, Window); 
        DestroyGPUDevice(Device);
    }
}

