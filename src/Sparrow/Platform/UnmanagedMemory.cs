﻿using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sparrow.Global;
using Sparrow.Platform;
using Sparrow.Platform.Posix;
using Sparrow.Platform.Win32;

namespace Sparrow
{
    public static unsafe class UnmanagedMemory
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntPtr Copy(byte* dest, byte* src, long count)
        {
            Debug.Assert(count >= 0);
            return PlatformDetails.RunningOnPosix
                ? Syscall.Copy(dest, src, count)
                : Win32UnmanagedMemory.Copy(dest, src, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Compare(byte* b1, byte* b2, long count)
        {
            Debug.Assert(count >= 0);
            return PlatformDetails.RunningOnPosix
                ? Syscall.Compare(b1, b2, count)
                : Win32UnmanagedMemory.Compare(b1, b2, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Move(byte* b1, byte* b2, long count)
        {
            Debug.Assert(count >= 0);
            return PlatformDetails.RunningOnPosix
                ? Syscall.Move(b1, b2, count)
                : Win32UnmanagedMemory.Move(b1, b2, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntPtr Set(byte* dest, int c, long count)
        {
            Debug.Assert(count >= 0);
            return PlatformDetails.RunningOnPosix
                ? Syscall.Set(dest, c, count)
                : Win32UnmanagedMemory.Set(dest, c, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte* Allocate4KAllignedMemory(long size)
        {
            Debug.Assert(size >= 0);

            if (PlatformDetails.RunningOnPosix)
            {
                var p = new IntPtr();
                var pp = new IntPtr(&p);
                Syscall.posix_memalign(pp, 4096, size);
                
                return (byte*)p.ToPointer();
            }

            return Win32MemoryProtectMethods.VirtualAlloc(null, (UIntPtr)size, Win32MemoryProtectMethods.AllocationType.COMMIT,
                Win32MemoryProtectMethods.MemoryProtection.READWRITE);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Free(byte* ptr)
        {
            var p = new IntPtr(ptr);
            if (PlatformDetails.RunningOnPosix)
            {
                Syscall.free(p);
                return;
            }

            Win32MemoryProtectMethods.VirtualFree(ptr, UIntPtr.Zero, Win32MemoryProtectMethods.FreeType.MEM_RELEASE);
        }
    }
}
