// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using Unity.Collections;

namespace Infohazard.HyperNav.Jobs {
    /// <summary>
    /// An implementation of a Min Heap that can be used with jobs and Burst.
    /// </summary>
    /// <remarks>
    /// The memory of the heap is allocated as a NativeList{T}.
    /// </remarks>
    /// <typeparam name="T">Element type of the heap.</typeparam>
    public struct NativeHeap<T> where T : unmanaged, IEquatable<T> {
        private NativeList<Element> _items;

        /// <summary>
        /// Current number of items in the heap.
        /// </summary>
        public int Count => _items.Length;

        /// <summary>
        /// Whether the heap has been allocated.
        /// </summary>
        public bool IsCreated => _items.IsCreated;

        /// <summary>
        /// Create a new NativeHeap.
        /// </summary>
        /// <param name="initialCapacity">The initial capacity to allocate.</param>
        /// <param name="allocator">The allocator to use.</param>
        public NativeHeap(int initialCapacity, Allocator allocator) {
            _items = new NativeList<Element>(initialCapacity, allocator);
        }

        /// <summary>
        /// Free the memory used by the heap.
        /// </summary>
        public void Dispose() => _items.Dispose();

        /// <summary>
        /// Remove all items from the heap.
        /// </summary>
        public void Clear() => _items.Clear();

        /// <summary>
        /// Add an item to the heap with the given priority.
        /// </summary>
        /// <param name="item">Item to add.</param>
        /// <param name="priority">The item's priority.</param>
        public void Add(T item, float priority) {
            _items.Add(new Element(item, priority));
            BubbleUp(_items.Length - 1);
        }

        /// <summary>
        /// Change the priority of an item in the heap, and optionally replace it with a new item.
        /// </summary>
        /// <param name="item">The item to change.</param>
        /// <param name="newPriority">The new priority to use.</param>
        /// <param name="replace">Whether to replace the item as well.</param>
        /// <param name="replaceWith">What item to replace it with.</param>
        /// <exception cref="ArgumentException">If item is not contained in the heap.</exception>
        public void Update(T item, float newPriority, bool replace = false, T replaceWith = default) {
            // Find index of item in the heap.
            int index = -1;
            for (int i = 0; i < _items.Length; i++) {
                if (_items[i].Item.Equals(item)) {
                    index = i;
                    break;
                }
            }
            
            if (index < 0) throw new ArgumentException("Value not in heap.");
            
            // Update the element's priority, and optionally its value.
            Element val = _items[index];
            float oldPriority = val.Priority;
            val = new Element(replace ? replaceWith : val.Item, newPriority);
            _items[index] = val;
            
            // "Bubble" the item to sort it to ensure the heap is still a min heap.
            if (newPriority > oldPriority) {
                BubbleUp(index);
            } else if (newPriority < oldPriority) {
                BubbleDown(index);
            }
        }

        /// <summary>
        /// Get the element at the top of the heap without removing it.
        /// </summary>
        /// <param name="value">The value at the top of the heap.</param>
        /// <returns>Whether the heap had an item to get.</returns>
        public bool TryPeek(out T value) {
            if (_items.Length > 0) {
                value = _items[0].Item;
                return true;
            } else {
                value = default;
                return false;
            }
        }

        /// <summary>
        /// Remove the element at the top of the heap and return it.
        /// </summary>
        /// <param name="value">The value that was at the top of the heap.</param>
        /// <returns>Whether the heap had an item to remove.</returns>
        public bool TryRemove(out T value) {
            if (!TryPeek(out value)) return false;
            
            // Swap last element with first (most efficient way to remove).
            _items[0] = _items[_items.Length - 1];
            _items.RemoveAt(_items.Length - 1);

            // Bubble swapped item down to maintain min heap.
            BubbleDown(0);
            return true;
        }

        private void Swap(int index1, int index2) {
            (_items[index1], _items[index2]) = (_items[index2], _items[index1]);
        }

        // Swap item with one of its children until it is in a valid place for a min heap.
        private void BubbleDown(int index) {
            while (index < _items.Length) {
                int leftIndex = index * 2 + 1;
                int rightIndex = index * 2 + 2;

                int largest = index;
                if (leftIndex < _items.Length && _items[leftIndex].Priority > _items[largest].Priority)
                    largest = leftIndex;
                if (rightIndex < _items.Length && _items[rightIndex].Priority > _items[largest].Priority)
                    largest = rightIndex;

                if (largest != index) {
                    Swap(largest, index);
                    index = largest;
                } else {
                    break;
                }
            }
        }

        // Swap item with its parent until it is in a valid place for a min heap.
        private void BubbleUp(int index) {
            while (index > 0) {
                int parentIndex = (index - 1) / 2;
                if (_items[index].Priority > _items[parentIndex].Priority) {
                    Swap(index, parentIndex);
                    index = parentIndex;
                } else {
                    break;
                }
            }
        }
        
        private readonly struct Element {
            public readonly T Item;
            public readonly float Priority;
        
            public Element(T item, float priority) {
                Item = item;
                Priority = priority;
            }
        }
    }
}
