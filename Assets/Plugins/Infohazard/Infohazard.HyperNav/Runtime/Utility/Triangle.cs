// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System;
using UnityEngine;

namespace Infohazard.HyperNav {
    /// <summary>
    /// Represents the indices of a triangle (three vertices by a face) in an indexed mesh.
    /// </summary>
    /// <remarks>
    /// The same Triangle will be created regardless of the order in which indices are fed to the constructor.
    /// </remarks>
    [Serializable]
    public struct Triangle : IEquatable<Triangle> {
        /// <summary>
        /// First vertex index, which is the lower of the three.
        /// </summary>
        public int Vertex1 => _minVertex;
        
        /// <summary>
        /// Second vertex index, which is the middle of the three.
        /// </summary>
        public int Vertex2 => _midVertex;
        
        /// <summary>
        /// Third vertex index, which is the larger of the three.
        /// </summary>
        public int Vertex3 => _maxVertex;

        /// <summary>
        /// (Serialized) First vertex index, which is the lower of the three.
        /// </summary>
        [SerializeField] private int _minVertex;
        
        /// <summary>
        /// (Serialized) Second vertex index, which is the middle of the three.
        /// </summary>
        [SerializeField] private int _midVertex;
        
        /// <summary>
        /// (Serialized) Third vertex index, which is the larger of the three.
        /// </summary>
        [SerializeField] private int _maxVertex;
        
        /// <summary>
        /// Construct a new Triangle with the given indices.
        /// </summary>
        /// <remarks>
        /// The order of the indices doesn't matter; the same Triangle is constructed either way.
        /// No two of the indices can be the same.
        /// </remarks>
        /// <param name="vertex1">First vertex index.</param>
        /// <param name="vertex2">Second vertex index.</param>
        /// <param name="vertex3">Third vertex index.</param>
        public Triangle(int vertex1, int vertex2, int vertex3) {
            if (vertex1 == vertex2 || vertex1 == vertex3 || vertex2 == vertex3) {
                Debug.LogError($"Triangle vertices must not be the same index: {vertex1}, {vertex2}, {vertex3}.");
            }

            // Ensure same triangle is created regardless of order.
            if (vertex1 > vertex2) {
                // vertex 1 > vertex2
                if (vertex3 > vertex1) {
                    // vertex3 > vertex1 > vertex2
                    _maxVertex = vertex3;
                    _minVertex = vertex2;
                    _midVertex = vertex1;
                } else if (vertex2 > vertex3) {
                    // vertex1 > vertex2 > vertex3
                    _maxVertex = vertex1;
                    _minVertex = vertex3;
                    _midVertex = vertex2;
                } else {
                    // vertex1 > vertex3 > vertex2
                    _maxVertex = vertex1;
                    _minVertex = vertex2;
                    _midVertex = vertex3;
                }
            } else {
                // vertex2 > vertex1
                if (vertex3 > vertex2) {
                    // vertex3 > vertex2 > vertex1
                    _maxVertex = vertex3;
                    _minVertex = vertex1;
                    _midVertex = vertex2;
                } else if (vertex3 > vertex1) {
                    // vertex2 > vertex3 > vertex1
                    _maxVertex = vertex2;
                    _minVertex = vertex1;
                    _midVertex = vertex3;
                } else {
                    // vertex2 > vertex1 > vertex3
                    _maxVertex = vertex2;
                    _minVertex = vertex3;
                    _midVertex = vertex1;
                }
            }
        }
        
        /// <summary>
        /// Compare to another object.
        /// </summary>
        /// <param name="obj">Object to compare to.</param>
        /// <returns>Whether the two objects are equal.</returns>
        public override bool Equals(object obj) {
            if (!(obj is Triangle triangle)) return false;
            return Equals(triangle);
        }
        
        /// <summary>
        /// Compare to another Triangle.
        /// </summary>
        /// <param name="other">Triangle to compare to.</param>
        /// <returns>Whether the two triangles are equal.</returns>
        public bool Equals(Triangle other) {
            return _minVertex == other._minVertex && _midVertex == other._midVertex && _maxVertex == other._maxVertex;
        }

        /// <summary>
        /// Get integer for use with hash table.
        /// </summary>
        /// <returns>Integer hash code.</returns>
        public override int GetHashCode() {
            return _minVertex ^ _midVertex ^ _maxVertex;
        }
    }
}