// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using UnityEngine;

namespace Infohazard.HyperNav {
    /// <summary>
    /// Represents the indices of an edge (two connected vertices) in an indexed mesh.
    /// </summary>
    /// <remarks>
    /// The same Edge will be created regardless of the order in which indices are fed to the constructor.
    /// </remarks>
    [Serializable]
    public struct Edge : IEquatable<Edge> {
        /// <summary>
        /// First vertex index, which is the lower of the two.
        /// </summary>
        public int Vertex1 => _minVertex;
        
        /// <summary>
        /// Second vertex index, which is the higher of the two.
        /// </summary>
        public int Vertex2 => _maxVertex;

        /// <summary>
        /// (Serialized) First vertex index, which is the lower of the two.
        /// </summary>
        [SerializeField] private int _minVertex;
        
        /// <summary>
        /// (Serialized) Second vertex index, which is the higher of the two.
        /// </summary>
        [SerializeField] private int _maxVertex;

        /// <summary>
        /// Construct a new Edge with the given indices.
        /// </summary>
        /// <remarks>
        /// The order of the indices doesn't matter; the same Edge is constructed either way.
        /// The indices cannot be the same.
        /// </remarks>
        /// <param name="vertex1">First vertex index.</param>
        /// <param name="vertex2">Second vertex index.</param>
        public Edge(int vertex1, int vertex2) {
            if (vertex1 == vertex2) {
                Debug.LogError($"Edge vertices must not be the same index: {vertex1}, {vertex2}.");
            }

            // Ensure same edge is created regardless of order.
            if (vertex1 > vertex2) {
                _minVertex = vertex2;
                _maxVertex = vertex1;
            } else {
                _minVertex = vertex1;
                _maxVertex = vertex2;
            }
        }

        /// <summary>
        /// Compare to another object.
        /// </summary>
        /// <param name="obj">Object to compare to.</param>
        /// <returns>Whether the two objects are equal.</returns>
        public override bool Equals(object obj) {
            if (!(obj is Edge edge)) return false;
            return Equals(edge);
        }
        
        /// <summary>
        /// Compare to another Edge.
        /// </summary>
        /// <param name="other">Edge to compare to.</param>
        /// <returns>Whether the two edges are equal.</returns>
        public bool Equals(Edge other) {
            return _minVertex == other._minVertex && _maxVertex == other._maxVertex;
        }

        /// <summary>
        /// Get integer for use with hash table.
        /// </summary>
        /// <returns>Integer hash code.</returns>
        public override int GetHashCode() {
            return _minVertex ^ _maxVertex;
        }
    }
}