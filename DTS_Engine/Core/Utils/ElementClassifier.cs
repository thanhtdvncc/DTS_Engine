using DTS_Engine.Core.Data;
using DTS_Engine.Core.Primitives;
using System;

namespace DTS_Engine.Core.Utils
{
    /// <summary>
    /// Centralized Logic for Classifying SAP Elements based on their Geometry.
    /// [v6.0] Updated to use:
    /// - AuditConfig for all thresholds (no more magic numbers)
    /// - Vector Math for Frame classification (independent of unit system)
    /// </summary>
    public static class ElementClassifier
    {
        public enum GlobalAxis
        {
            Unknown = 0,
            PositiveX = 1,
            NegativeX = 2,
            PositiveY = 3,
            NegativeY = 4,
            PositiveZ = 5,
            NegativeZ = 6,
            Mix = 99 // Oblique/Skewed
        }

        public enum ElementType
        {
            Unknown,
            Beam,
            Column,
            Trace, // Vertical but small/short
            Wall,
            Slab,
            ObliqueFrame,
            ObliqueArea
        }

        /// <summary>
        /// Analyzes a vector (usually L3 Normal) to determine its Global Axis alignment.
        /// Uses AuditConfig.STRICT_AXIS_THRESHOLD instead of hardcoded 0.9
        /// </summary>
        public static void AnalyzeGlobalAxis(Vector3D normalVector, out string axisName, out int sign, out GlobalAxis axisType)
        {
            double gx = normalVector.X;
            double gy = normalVector.Y;
            double gz = normalVector.Z;

            double threshold = AuditConfig.STRICT_AXIS_THRESHOLD;

            if (Math.Abs(gx) > threshold)
            {
                axisName = gx > 0 ? "Global +X" : "Global -X";
                sign = gx > 0 ? 1 : -1;
                axisType = gx > 0 ? GlobalAxis.PositiveX : GlobalAxis.NegativeX;
            }
            else if (Math.Abs(gy) > threshold)
            {
                axisName = gy > 0 ? "Global +Y" : "Global -Y";
                sign = gy > 0 ? 1 : -1;
                axisType = gy > 0 ? GlobalAxis.PositiveY : GlobalAxis.NegativeY;
            }
            else if (Math.Abs(gz) > threshold)
            {
                axisName = gz > 0 ? "Global +Z" : "Global -Z";
                sign = gz > 0 ? 1 : -1;
                axisType = gz > 0 ? GlobalAxis.PositiveZ : GlobalAxis.NegativeZ;
            }
            else
            {
                axisName = "Mix";
                sign = 1;
                axisType = GlobalAxis.Mix;
            }
        }

        /// <summary>
        /// Determines if a FRAME is Column, Beam, or Oblique.
        /// [v6.0] Uses VECTOR MATH instead of Length2D to avoid unit-dependent issues.
        /// 
        /// Logic:
        /// 1. Get Local Axis 1 (L1) from SAP API - this is the direction along the frame length
        /// 2. Check L1.Z component:
        ///    - If |L1.Z| > threshold (0.5) -> Frame is mostly vertical -> Column
        ///    - If |L1.Z| < threshold -> Frame is mostly horizontal -> Beam
        /// 
        /// This approach is unit-independent because it uses normalized direction vectors.
        /// </summary>
        public static ElementType DetermineFrameType(SapFrame frame)
        {
            if (frame == null) return ElementType.Unknown;

            // [v6.0] VECTOR MATH: Get L1 from API (direction along frame length)
            var vectors = SapUtils.GetElementVectors(frame.Name);
            if (vectors != null)
            {
                Vector3D l1 = vectors.Value.L1;

                // If L1.Z is large, frame is vertical/steeply inclined -> Column
                if (Math.Abs(l1.Z) > AuditConfig.VERTICAL_AXIS_THRESHOLD)
                {
                    return ElementType.Column;
                }
                else
                {
                    return ElementType.Beam;
                }
            }

            // Fallback: Geometry-based check (if API fails)
            // Use Length2D/Length3D ratio
            if (frame.IsVertical) return ElementType.Column;

            double length3D = frame.Length3D;
            if (length3D < 1e-6) return ElementType.Column; // Point-like

            double slopeRatio = frame.Length2D / length3D;

            // If projected < 50% of real length -> steep slope (>60 deg) -> Column
            if (slopeRatio < AuditConfig.VERTICAL_AXIS_THRESHOLD)
            {
                return ElementType.Column;
            }
            else
            {
                return ElementType.Beam;
            }
        }

        /// <summary>
        /// Determines if a FRAME is Column or Beam using ONLY Vector data.
        /// This is the preferred method when L1 vector is available from ModelInventory.
        /// </summary>
        public static ElementType DetermineFrameTypeByVector(Vector3D l1)
        {
            // If L1.Z is large, frame is vertical/steeply inclined -> Column
            if (Math.Abs(l1.Z) > AuditConfig.VERTICAL_AXIS_THRESHOLD)
            {
                return ElementType.Column;
            }
            else
            {
                return ElementType.Beam;
            }
        }

        /// <summary>
        /// Determines if an AREA is Wall, Slab, or Oblique.
        /// Uses the Normal Vector (L3).
        /// </summary>
        public static ElementType DetermineAreaType(Vector3D normalL3)
        {
            AnalyzeGlobalAxis(normalL3, out _, out _, out var axis);

            switch (axis)
            {
                case GlobalAxis.PositiveZ:
                case GlobalAxis.NegativeZ:
                    return ElementType.Slab; // Normal is Z -> Surface is XY -> Slab

                case GlobalAxis.PositiveX:
                case GlobalAxis.NegativeX:
                case GlobalAxis.PositiveY:
                case GlobalAxis.NegativeY:
                    return ElementType.Wall; // Normal is X or Y -> Surface is Vertical -> Wall

                case GlobalAxis.Mix:
                default:
                    return ElementType.ObliqueArea;
            }
        }
    }
}
