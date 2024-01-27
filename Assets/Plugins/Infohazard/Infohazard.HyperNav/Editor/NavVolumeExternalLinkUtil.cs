// This file is part of the HyperNav package.
// Copyright (c) 2022-present Vincent Miller (Infohazard Games).

using System.Collections.Generic;
using System.Linq;
using Infohazard.Core;
using Infohazard.HyperNav;
using UnityEditor;
using UnityEngine;

namespace Infohazard.HyperNav.Editor {
    /// <summary>
    /// Utilities for generating the external links of <see cref="NavVolume"/>s.
    /// </summary>
    public static class NavVolumeExternalLinkUtil {
        /// <summary>
        /// Generate the external links for all loaded <see cref="NavVolume"/>s.
        /// </summary>
        public static void GenerateAllExternalLinks() {
            foreach (NavVolume volume in Object.FindObjectsOfType<NavVolume>()) {
                if (volume.Data != null) {
                    GenerateExternalLinks(volume);
                }
            }
        }
        
        /// <summary>
        /// Generate the external links for a specific <see cref="NavVolume"/>.
        /// </summary>
        /// <param name="volume"></param>
        public static void GenerateExternalLinks(NavVolume volume) {
            float selfRadius = volume.Bounds.extents.magnitude;
            Vector3 selfCenter = volume.transform.TransformPoint(volume.Bounds.center);

            // Find list of all volumes that could share eternal links with this one.
            List<NavVolume> otherVolumes = Object.FindObjectsOfType<NavVolume>().Where(otherVolume => {
                if (otherVolume == volume || !otherVolume.Data) return false;

                float otherRadius = otherVolume.Bounds.extents.magnitude;
                Vector3 otherCenter = otherVolume.transform.TransformPoint(otherVolume.Bounds.center);

                // If radius + MaxExternalLinkDistance doesn't overlap with other volume, cannot have any links.
                float distance = Vector3.Distance(selfCenter, otherCenter);
                if (distance > otherRadius + selfRadius + volume.MaxExternalLinkDistance) return false;

                return true;
            }).ToList();

            // Cache square distance.
            float maxDistSqr = volume.MaxExternalLinkDistance * volume.MaxExternalLinkDistance;
            
            // Find external links for each region.
            Undo.RecordObject(volume.Data, "Generate HyperNav Links");
            foreach (NavRegionData region in volume.Data.Regions) {
                List<NavExternalLinkData> links = new List<NavExternalLinkData>();
                foreach (NavVolume otherVolume in otherVolumes) {
                    foreach (NavRegionData otherRegion in otherVolume.Data.Regions) {
                        // Check if self region has a link to other region.
                        Vector3 nearestOnSelf = GetNearestPointOnRegion(
                            volume, region.ID, otherVolume.transform.TransformPoint(otherRegion.Bounds.center));
                        Vector3 nearestOnOther = GetNearestPointOnRegion(
                            otherVolume, otherRegion.ID, nearestOnSelf);

                        if (Vector3.SqrMagnitude(nearestOnOther - nearestOnSelf) < maxDistSqr) {
                            Vector3 nearestOnSelfLocal = volume.transform.InverseTransformPoint(nearestOnSelf);
                            Vector3 nearestOnOtherLocal = volume.transform.InverseTransformPoint(nearestOnOther);
                            
                            links.Add(NavExternalLinkData.Create(otherVolume.InstanceID, otherRegion.ID,
                                                                 nearestOnSelfLocal, nearestOnOtherLocal));
                        }
                    }
                }
                region.SetExternalLinks(links.ToArray());
            }
            volume.Data.MarkExternalLinksLocalSpace();
            
            EditorUtility.SetDirty(volume.Data);
            AssetDatabase.SaveAssets();
        }

        // Get the nearest point on any of a region's triangles, edges, or vertices to the given point.
        private static Vector3 GetNearestPointOnRegion(NavVolume volume, int regionIndex, Vector3 point) {
            Vector3 localPos = volume.transform.InverseTransformPoint(point);
            
            Vector3 closestPoint = default;
            float closestDistance = float.PositiveInfinity;

            NavRegionData region = volume.Data.Regions[regionIndex];

            // Loop through all triangles, and check each one for closest point.
            int triCount = region.Indices.Count / 3;
            for (int triIndex = 0; triIndex < triCount; triIndex++) {
                int triStart = triIndex * 3;
                
                int v1 = region.Indices[triStart + 0];
                int v2 = region.Indices[triStart + 1];
                int v3 = region.Indices[triStart + 2];

                Vector3 v1Pos = volume.Data.Vertices[v1];
                Vector3 v2Pos = volume.Data.Vertices[v2];
                Vector3 v3Pos = volume.Data.Vertices[v3];

                Vector3 testPos =
                    MathUtility.GetNearestPointOnTriangleIncludingBounds(v1Pos, v2Pos, v3Pos, localPos);
                
                float dist2 = Vector3.SqrMagnitude(testPos - localPos);
                if (dist2 < closestDistance) {
                    closestPoint = testPos;
                    closestDistance = dist2;
                }
            }

            return volume.transform.TransformPoint(closestPoint);
        }
    }
}