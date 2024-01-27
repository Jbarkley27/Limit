// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Infohazard.HyperNav.Jobs {
    /// <summary>
    /// Job that performs a single raycast in a <see cref="NavVolume"/>.
    /// </summary>
    [BurstCompile]
    public struct NavRaycastJob : IJob {
        /// <summary>
        /// Volume to raycast in.
        /// </summary>
        public NativeNavVolumeData Volume;
        
        /// <summary>
        /// Start point of the segment.
        /// </summary>
        public float4 Start;
        
        /// <summary>
        /// End point of the segment.
        /// </summary>
        public float4 End;

        /// <summary>
        /// A single-element array where the hit distance (or -1 if no hit) is written.
        /// </summary>
        [WriteOnly] public NativeArray<float> OutDistance;

        /// <summary>
        /// Execute the raycast.
        /// </summary>
        public void Execute() {
            bool hit = NativeMathUtility.NavRaycast(Start, End, false, Volume, out float t);
            OutDistance[0] = hit ? t : -1;
        }
    }

    /// <summary>
    /// A single raycast in a <see cref="NavMultiRaycastJob"/>.
    /// </summary>
    public struct NativeRaycastElement {
        /// <summary>
        /// Volume to raycast in.
        /// </summary>
        public long VolumeID;
        
        /// <summary>
        /// Where the hit distance (or -1 if no hit) of the raycast is written.
        /// </summary>
        public float OutDistance;
        
        /// <summary>
        /// Start point of the segment.
        /// </summary>
        public float4 Start;
        
        /// <summary>
        /// End point of the segment.
        /// </summary>
        public float4 End;
    }

    /// <summary>
    /// Job that performs multiple raycasts in one or more <see cref="NavVolume"/>s in parallel.
    /// </summary>
    [BurstCompile]
    public struct NavMultiRaycastJob : IJobParallelFor {
        /// <summary>
        /// All loaded volume data.
        /// </summary>
        [ReadOnly] public NativeParallelHashMap<long, NativeNavVolumeData> Volumes;
        
        /// <summary>
        /// The raycasts to perform (results are stored in each element's <see cref="NativeRaycastElement.OutDistance"/>.
        /// </summary>
        public NativeArray<NativeRaycastElement> Raycasts;

        /// <summary>
        /// Execute the job for the raycast at index.
        /// </summary>
        /// <param name="index">Index of the raycast to execute.</param>
        public void Execute(int index) {
            NativeRaycastElement raycast = Raycasts[index];
            if (Volumes.TryGetValue(raycast.VolumeID, out NativeNavVolumeData volume)) {
                bool didHit = NativeMathUtility.NavRaycast(raycast.Start, raycast.End, false, volume, out float t);
                raycast.OutDistance = didHit ? t : -1;
            } else {
                raycast.OutDistance = -1;
            }

            Raycasts[index] = raycast;
        }
    }
}