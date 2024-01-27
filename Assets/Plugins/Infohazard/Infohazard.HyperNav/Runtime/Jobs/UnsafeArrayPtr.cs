// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Infohazard.HyperNav.Jobs {
    /// <summary>
    /// This is a simple wrapper for unmanaged memory which bypasses Unity's safety checks.
    /// This allows arrays to be nested in other arrays (or in structs contained in arrays).
    /// Note that you must keep a reference to the original NativeArray, or Unity will detect a memory leak.
    /// </summary>
    /// <typeparam name="T">Element type of the array.</typeparam>
    public readonly struct UnsafeArrayPtr<T> : IDisposable where T : unmanaged {
        /// <summary>
        /// Length of the array.
        /// </summary>
        public readonly int Length;
        
        /// <summary>
        /// Pointer to the start of the array.
        /// </summary>
        [NativeDisableUnsafePtrRestriction]
        public readonly IntPtr Pointer;
        
        /// <summary>
        /// Allocator used to allocate the array, or None if it was created from a NativeArray.
        /// </summary>
        public readonly Allocator Allocator;

        /// <summary>
        /// Get a reference to the element at the given index (can be used to set values as well).
        /// </summary>
        /// <param name="index">The index.</param>
        /// <exception cref="InvalidOperationException">(Dev Only) If underlying array is not set.</exception>
        /// <exception cref="IndexOutOfRangeException">(Dev Only) If index is outside the bounds of the array.</exception>
        public unsafe ref T this[int index] {
            get {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (Pointer == IntPtr.Zero) throw new InvalidOperationException();
                if (index < 0 || index > Length) throw new IndexOutOfRangeException();
#endif

                return ref UnsafeUtility.ArrayElementAsRef<T>((void*) Pointer, index);
            }
        }

        private UnsafeArrayPtr(int length, IntPtr ptr, Allocator allocator) {
            Length = length;
            Pointer = ptr;
            Allocator = allocator;
        }
        
        /// <summary>
        /// Free the memory if it has been allocated directly.
        /// </summary>
        /// <remarks>
        /// If this pointer is wrapping a NativeArray, this does nothing.
        /// </remarks>
        public unsafe void Dispose() {
            if (Allocator > Allocator.None) {
                UnsafeUtility.Free((void*)Pointer, Allocator);
            }
        }

        /// <summary>
        /// Create a pointer to the given NativeArray.
        /// </summary>
        /// <param name="array">Array to create a pointer to.</param>
        /// <returns>The created pointer.</returns>
        public static unsafe UnsafeArrayPtr<T> ToPointer(in NativeArray<T> array) {
            return new UnsafeArrayPtr<T>(array.Length, (IntPtr) array.GetUnsafePtr(), Allocator.None);
        }
    }
}