using System;

namespace KnobForge.Rendering.GPU;

public interface IMTLLibrary
{
    IntPtr Handle { get; }
}

public interface IMTLFunction
{
    IntPtr Handle { get; }
}

public interface IMTLRenderPipelineState
{
    IntPtr Handle { get; }
}

public interface IMTLBuffer : IDisposable
{
    IntPtr Handle { get; }
}

public interface IMTLRenderCommandEncoder
{
    IntPtr Handle { get; }
}

public enum MTLIndexType : uint
{
    UInt16 = 0,
    UInt32 = 1
}

public readonly struct MTLRenderCommandEncoderHandle : IMTLRenderCommandEncoder
{
    public IntPtr Handle { get; }

    public MTLRenderCommandEncoderHandle(IntPtr handle)
    {
        Handle = handle;
    }
}
