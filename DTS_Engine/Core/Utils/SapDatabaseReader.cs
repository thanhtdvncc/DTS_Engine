using DTS_Engine.Core.Data;
using DTS_Engine.Core.Engines;
using DTS_Engine.Core.Primitives;
using SAP2000v1;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DTS_Engine.Core.Utils
{
    /// <summary>
    /// Simplified SAP reader that uses SapUtils object-level helpers.
    /// This avoids direct cSapModel interop signatures and relies on SapUtils to provide raw loads.
    /// </summary>
    public class SapDatabaseReader
    {
        private readonly cSapModel _model;
        private readonly ModelInventory _inventory;

        public SapDatabaseReader(cSapModel model, ModelInventory inventory)
        {
            _model = model;
            _inventory = inventory;
        }

        public List<RawSapLoad> ReadAllLoads(string pattern)
        {
            var loads = new List<RawSapLoad>();

            // Use SapUtils high-level readers if available
            try
            {
                var frameDist = SapUtils.GetAllFrameDistributedLoads(pattern) ?? new List<RawSapLoad>();
                var areaUnif = SapUtils.GetAllAreaUniformLoads(pattern) ?? new List<RawSapLoad>();
                var areaToFrame = SapUtils.GetAllAreaUniformToFrameLoads(pattern) ?? new List<RawSapLoad>();
                var framePoints = SapUtils.GetAllFramePointLoads(pattern) ?? new List<RawSapLoad>();
                var pointLoads = SapUtils.GetAllPointLoads(pattern) ?? new List<RawSapLoad>();

                loads.AddRange(frameDist);
                loads.AddRange(areaUnif);
                loads.AddRange(areaToFrame);
                loads.AddRange(framePoints);
                loads.AddRange(pointLoads);
            }
            catch
            {
                // If SapUtils methods are not available or fail, return empty list
                return new List<RawSapLoad>();
            }

            // Ensure Direction components are set using inventory if missing
            foreach (var l in loads)
            {
                // If already set (non-zero) skip
                if (Math.Abs(l.DirectionX) > 1e-9 || Math.Abs(l.DirectionY) > 1e-9 || Math.Abs(l.DirectionZ) > 1e-9)
                    continue;

                double magnitude = l.Value1; // assume SapUtils returned converted magnitude
                try
                {
                    var vec = CalculateVector(l.ElementName, magnitude, l.Direction, l.CoordSys);
                    l.DirectionX = vec.X;
                    l.DirectionY = vec.Y;
                    l.DirectionZ = vec.Z;
                }
                catch
                {
                    // fallback: assume gravity
                    l.DirectionX = 0; l.DirectionY = 0; l.DirectionZ = -magnitude;
                }
            }

            return loads;
        }

        private Vector3D CalculateVector(string el, double val, string dir, string sys)
        {
            // Global case
            if (string.IsNullOrEmpty(sys) || sys.ToUpperInvariant().Contains("GLOBAL"))
            {
                var d = (dir ?? "GRAVITY").ToUpperInvariant();
                if (d.Contains("GRAVITY") || d.Contains("Z")) return new Vector3D(0, 0, -val);
                if (d.Contains("X")) return new Vector3D(val, 0, 0);
                if (d.Contains("Y")) return new Vector3D(0, val, 0);
                return new Vector3D(0, 0, -val);
            }

            // Local case: use inventory axes
            if (_inventory != null)
            {
                int axis = 3;
                var d = (dir ?? "").ToUpperInvariant();
                if (d.Contains("1") || d.Contains("X")) axis = 1;
                if (d.Contains("2") || d.Contains("Y")) axis = 2;

                var local = _inventory.GetLocalAxis(el, axis);
                if (local.HasValue)
                    return val * local.Value;
            }

            // fallback
            return new Vector3D(0, 0, -Math.Abs(val));
        }
    }
}
