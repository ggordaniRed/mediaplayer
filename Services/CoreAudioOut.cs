using System;
using System.Runtime.InteropServices;

namespace MediaPlayer.Services;

sealed class CoreAudioOut : IDisposable
{
    private const string AT =
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    [DllImport(AT)] private static extern int AudioQueueNewOutput(
        ref ASBD fmt, Callback cb, IntPtr ud, IntPtr rl, IntPtr rm, uint fl, out IntPtr aq);
    [DllImport(AT)] private static extern int AudioQueueAllocateBuffer(
        IntPtr aq, uint sz, out IntPtr buf);
    [DllImport(AT)] private static extern int AudioQueueEnqueueBuffer(
        IntPtr aq, IntPtr buf, uint n, IntPtr p);
    [DllImport(AT)] private static extern int AudioQueueStart(IntPtr aq, IntPtr t);
    [DllImport(AT)] private static extern int AudioQueueStop(IntPtr aq, byte imm);
    [DllImport(AT)] private static extern int AudioQueueDispose(IntPtr aq, byte imm);

    [StructLayout(LayoutKind.Sequential)]
    private struct ASBD
    {
        public double SR;
        public uint FmtID, Flags, BPP, FPP, BPF, CPF, BPC, Res;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Callback(IntPtr ud, IntPtr aq, IntPtr buf);

    // 2 seconds of S16 stereo @ 44100Hz
    private const int RingSize = 44100 * 2 * 2 * 2;
    private readonly byte[] _ring = new byte[RingSize];
    private volatile int _writePos;
    private volatile int _readPos;

    private IntPtr _aq;
    private readonly int _bufBytes;
    private readonly Callback _cb;
    private volatile bool _dead;

    public CoreAudioOut(uint sampleRate, uint channels)
    {
        _bufBytes = 4096 * (int)channels * 2;

        var fmt = new ASBD
        {
            SR = sampleRate, FmtID = 0x6C70636D, Flags = 12,
            BPP = channels * 2, FPP = 1, BPF = channels * 2,
            CPF = channels, BPC = 16
        };

        _cb = OnBuffer;
        AudioQueueNewOutput(ref fmt, _cb, IntPtr.Zero,
            IntPtr.Zero, IntPtr.Zero, 0, out _aq);

        for (int i = 0; i < 4; i++)
        {
            AudioQueueAllocateBuffer(_aq, (uint)_bufBytes, out var buf);
            var ad = Marshal.ReadIntPtr(buf, 8);
            unsafe { new Span<byte>((byte*)ad, _bufBytes).Clear(); }
            Marshal.WriteInt32(buf, 16, _bufBytes);
            AudioQueueEnqueueBuffer(_aq, buf, 0, IntPtr.Zero);
        }
        AudioQueueStart(_aq, IntPtr.Zero);
    }

    public unsafe void EnqueueS16(IntPtr samples, int byteCount)
    {
        if (_dead) return;
        byte* src = (byte*)samples;
        int wp = _writePos;

        // Fast path: contiguous write
        int spaceToEnd = RingSize - wp;
        if (byteCount <= spaceToEnd)
        {
            fixed (byte* dst = &_ring[wp])
                Buffer.MemoryCopy(src, dst, spaceToEnd, byteCount);
            _writePos = (wp + byteCount) % RingSize;
        }
        else
        {
            // Wrap around
            fixed (byte* dst = &_ring[wp])
                Buffer.MemoryCopy(src, dst, spaceToEnd, spaceToEnd);
            int remainder = byteCount - spaceToEnd;
            fixed (byte* dst = &_ring[0])
                Buffer.MemoryCopy(src + spaceToEnd, dst, RingSize, remainder);
            _writePos = remainder;
        }
    }

    private void OnBuffer(IntPtr ud, IntPtr aq, IntPtr buf)
    {
        if (_dead) return;
        var audioData = Marshal.ReadIntPtr(buf, 8);

        int w = _writePos, r = _readPos;
        int avail = w >= r ? w - r : RingSize - r + w;
        int toRead = Math.Min(avail, _bufBytes);

        unsafe
        {
            byte* dst = (byte*)audioData;

            if (toRead > 0)
            {
                int spaceToEnd = RingSize - r;
                if (toRead <= spaceToEnd)
                {
                    fixed (byte* src = &_ring[r])
                        Buffer.MemoryCopy(src, dst, _bufBytes, toRead);
                }
                else
                {
                    fixed (byte* src = &_ring[r])
                        Buffer.MemoryCopy(src, dst, _bufBytes, spaceToEnd);
                    int remainder = toRead - spaceToEnd;
                    fixed (byte* src = &_ring[0])
                        Buffer.MemoryCopy(src, dst + spaceToEnd, _bufBytes - spaceToEnd, remainder);
                }
                _readPos = (r + toRead) % RingSize;
            }

            // Zero remaining
            if (toRead < _bufBytes)
                new Span<byte>(dst + toRead, _bufBytes - toRead).Clear();
        }

        Marshal.WriteInt32(buf, 16, _bufBytes);
        AudioQueueEnqueueBuffer(aq, buf, 0, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_dead) return;
        _dead = true;
        AudioQueueStop(_aq, 1);
        AudioQueueDispose(_aq, 1);
    }
}
