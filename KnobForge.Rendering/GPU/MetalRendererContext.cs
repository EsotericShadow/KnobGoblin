using System;
using System.Runtime.InteropServices;

namespace KnobForge.Rendering.GPU;

public interface IMTLDevice
{
    IntPtr Handle { get; }
    IMTLCommandQueue NewCommandQueue();
    IMTLBuffer NewBufferWithBytes(IntPtr bytes, nuint length, nuint options);
}

public interface IMTLCommandQueue
{
    IntPtr Handle { get; }
    IMTLCommandBuffer CommandBuffer();
}

public interface IMTLCommandBuffer
{
    IntPtr Handle { get; }
}

public enum MTLPixelFormat : uint
{
    MTLPixelFormatBGRA8Unorm = 80
}

public sealed class MetalRendererContext
{
    private static readonly Lazy<MetalRendererContext> InstanceFactory = new(static () => new MetalRendererContext());

    public static MetalRendererContext Instance => InstanceFactory.Value;

    public IMTLDevice Device { get; }

    public IMTLCommandQueue CommandQueue { get; }

    public const MTLPixelFormat DefaultColorFormat = MTLPixelFormat.MTLPixelFormatBGRA8Unorm;

    private MetalRendererContext()
    {
        IntPtr deviceHandle = IntPtr.Zero;

        if (OperatingSystem.IsMacOS())
        {
            deviceHandle = MetalInterop.MTLCreateSystemDefaultDevice();
        }

        Device = new MTLDeviceHandle(deviceHandle);
        CommandQueue = Device.NewCommandQueue();
    }

    public IMTLCommandBuffer CreateCommandBuffer()
    {
        return CommandQueue.CommandBuffer();
    }

    public IMTLBuffer CreateBuffer<T>(ReadOnlySpan<T> data, nuint options = 0) where T : unmanaged
    {
        if (data.IsEmpty)
        {
            return new MTLBufferHandle(IntPtr.Zero);
        }

        T[] managed = data.ToArray();
        nuint bytesLength = (nuint)(managed.Length * Marshal.SizeOf<T>());
        GCHandle pinned = GCHandle.Alloc(managed, GCHandleType.Pinned);
        try
        {
            return Device.NewBufferWithBytes(pinned.AddrOfPinnedObject(), bytesLength, options);
        }
        finally
        {
            pinned.Free();
        }
    }

    private sealed class MTLDeviceHandle : IMTLDevice
    {
        public IntPtr Handle { get; }

        public MTLDeviceHandle(IntPtr handle)
        {
            Handle = handle;
        }

        public IMTLCommandQueue NewCommandQueue()
        {
            if (Handle == IntPtr.Zero)
            {
                return new MTLCommandQueueHandle(IntPtr.Zero);
            }

            IntPtr queueHandle = ObjC.IntPtr_objc_msgSend(Handle, Selectors.NewCommandQueue);
            return new MTLCommandQueueHandle(queueHandle);
        }

        public IMTLBuffer NewBufferWithBytes(IntPtr bytes, nuint length, nuint options)
        {
            if (Handle == IntPtr.Zero || bytes == IntPtr.Zero || length == 0)
            {
                return new MTLBufferHandle(IntPtr.Zero);
            }

            IntPtr bufferHandle = ObjC.IntPtr_objc_msgSend_IntPtr_UInt_UInt(
                Handle,
                Selectors.NewBufferWithBytesLengthOptions,
                bytes,
                length,
                options);

            return new MTLBufferHandle(bufferHandle);
        }
    }

    private sealed class MTLCommandQueueHandle : IMTLCommandQueue
    {
        public IntPtr Handle { get; }

        public MTLCommandQueueHandle(IntPtr handle)
        {
            Handle = handle;
        }

        public IMTLCommandBuffer CommandBuffer()
        {
            if (Handle == IntPtr.Zero)
            {
                return new MTLCommandBufferHandle(IntPtr.Zero);
            }

            IntPtr commandBufferHandle = ObjC.IntPtr_objc_msgSend(Handle, Selectors.CommandBuffer);
            return new MTLCommandBufferHandle(commandBufferHandle);
        }
    }

    private sealed class MTLCommandBufferHandle : IMTLCommandBuffer
    {
        public IntPtr Handle { get; }

        public MTLCommandBufferHandle(IntPtr handle)
        {
            Handle = handle;
        }
    }

    private sealed class MTLBufferHandle : IMTLBuffer
    {
        public IntPtr Handle { get; }

        public MTLBufferHandle(IntPtr handle)
        {
            Handle = handle;
        }
    }

    private static class Selectors
    {
        public static readonly IntPtr NewCommandQueue = ObjC.sel_registerName("newCommandQueue");
        public static readonly IntPtr CommandBuffer = ObjC.sel_registerName("commandBuffer");
        public static readonly IntPtr NewBufferWithBytesLengthOptions = ObjC.sel_registerName("newBufferWithBytes:length:options:");
    }

    private static class MetalInterop
    {
        [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
        public static extern IntPtr MTLCreateSystemDefaultDevice();
    }

    private static class ObjC
    {
        [DllImport("/usr/lib/libobjc.A.dylib")]
        public static extern IntPtr sel_registerName(string name);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        public static extern IntPtr IntPtr_objc_msgSend_IntPtr_UInt_UInt(
            IntPtr receiver,
            IntPtr selector,
            IntPtr arg1,
            nuint arg2,
            nuint arg3);
    }
}
