// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

namespace Infohazard.HyperNav.Editor {
    /// <summary>
    /// A data structure equivalent to a three-dimensional int array (int[,,]), but more efficient.
    /// </summary>
    /// <remarks>
    /// Unlike an int[,,], this type does not perform bounds checking on each dimension.
    /// The performance is equivalent to using a single-dimension array and doing the index math yourself.
    /// </remarks>
    public struct Fast3DArray {
        /// <summary>
        /// Size of first dimension.
        /// </summary>
        public readonly int SizeX;
        
        /// <summary>
        /// Size of second dimension.
        /// </summary>
        public readonly int SizeY;
        
        /// <summary>
        /// Size of third dimension.
        /// </summary>
        public readonly int SizeZ;

        private int[] _array;

        /// <summary>
        /// Construct a new Fast3DArray with the given dimensions.
        /// </summary>
        /// <param name="sizeX">Size of first dimension.</param>
        /// <param name="sizeY">Size of second dimension.</param>
        /// <param name="sizeZ">Size of third dimension.</param>
        public Fast3DArray(int sizeX, int sizeY, int sizeZ) {
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            _array = new int[sizeX * sizeY * sizeZ];
        }

        /// <summary>
        /// Get or set the value at given coordinates.
        /// </summary>
        /// <param name="x">First coordinate.</param>
        /// <param name="y">Second coordinate.</param>
        /// <param name="z">Third coordinate.</param>
        public int this[int x, int y, int z] {
            get => _array[ToIndex(x, y, z)];
            set => _array[ToIndex(x, y, z)] = value;
        }

        private int ToIndex(int x, int y, int z) {
            return z * SizeX * SizeY + y * SizeX + x;
        }

        /// <summary>
        /// Return true if the element at [x, y, z] is either option1 or option2.
        /// </summary>
        /// <param name="x">First coordinate.</param>
        /// <param name="y">Second coordinate.</param>
        /// <param name="z">Third coordinate.</param>
        /// <param name="option1">First option to check equality.</param>
        /// <param name="option2">Second option to check equality.</param>
        /// <returns>If the value at the given coordinates is equal to either option1 or option2.</returns>
        public bool IsOneOf(int x, int y, int z, int option1, int option2) {
            int value = _array[ToIndex(x, y, z)];
            return value == option1 || value == option2;
        }
    }
}