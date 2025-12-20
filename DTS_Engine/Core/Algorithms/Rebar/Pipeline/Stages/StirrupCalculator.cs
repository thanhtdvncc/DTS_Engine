using System;
using System.Collections.Generic;
using System.Linq;
using DTS_Engine.Core.Algorithms.Rebar.Models;
using DTS_Engine.Core.Data;

namespace DTS_Engine.Core.Algorithms.Rebar.Pipeline.Stages
{
    /// <summary>
    /// Stage 3: Tính toán cốt đai dựa trên ShearArea và TTArea từ SAP2000.
    /// Sử dụng logic đã có trong RebarCalculator.CalculateStirrup.
    /// Context vào → Context với CurrentSolution đã có StirrupDesign.
    /// </summary>
    public class StirrupCalculator : IRebarPipelineStage
    {
        public string StageName { get { return "StirrupCalculator"; } }
        public int Order { get { return 3; } }

        public IEnumerable<SolutionContext> Execute(
            IEnumerable<SolutionContext> inputs,
            ProjectConstraints globalConstraints)
        {
            foreach (var ctx in inputs)
            {
                if (!ctx.IsValid)
                {
                    yield return ctx;
                    continue;
                }

                CalculateStirrupsForSolution(ctx);
                yield return ctx;
            }
        }

        /// <summary>
        /// Tính toán cốt đai cho từng vị trí trong dầm (Left, Mid, Right của mỗi nhịp).
        /// </summary>
        private void CalculateStirrupsForSolution(SolutionContext ctx)
        {
            var group = ctx.Group;
            var results = ctx.SpanResults;
            var settings = ctx.Settings;
            var sol = ctx.CurrentSolution;

            if (sol == null || results == null || group?.Spans == null) return;

            int numSpans = Math.Min(group.Spans.Count, results.Count);

            // Initialize StirrupDesigns dictionary if not exists
            if (sol.StirrupDesigns == null)
                sol.StirrupDesigns = new Dictionary<string, string>();

            for (int i = 0; i < numSpans; i++)
            {
                var span = group.Spans[i];
                var res = results[i];
                if (res == null) continue;

                double beamWidth = span.Width > 0 ? span.Width : ctx.BeamWidth;

                // Calculate stirrup for 3 positions: Left (0), Mid (1), Right (2)
                for (int pos = 0; pos < 3; pos++)
                {
                    string posName = pos == 0 ? "Left" : pos == 1 ? "Mid" : "Right";
                    string key = string.Format("{0}_Stirrup_{1}", span.SpanId, posName);

                    double shearArea = GetValueAt(res.ShearArea, pos);
                    double ttArea = GetValueAt(res.TTArea, pos);

                    // Use existing CalculateStirrup logic from RebarCalculator
                    string stirrupResult = Algorithms.RebarCalculator.CalculateStirrup(
                        shearArea, ttArea, beamWidth, settings
                    );

                    sol.StirrupDesigns[key] = stirrupResult;
                }
            }

            // Also store the "governing" stirrup for each span (max Av/s position)
            for (int i = 0; i < numSpans; i++)
            {
                var span = group.Spans[i];
                var res = results[i];
                if (res == null) continue;

                double beamWidth = span.Width > 0 ? span.Width : ctx.BeamWidth;

                // Find max shear position (usually at supports for simply-supported)
                double maxShear = 0;
                double maxTT = 0;
                for (int pos = 0; pos < 3; pos++)
                {
                    double s = GetValueAt(res.ShearArea, pos);
                    double t = GetValueAt(res.TTArea, pos);
                    if (s + 2 * t > maxShear + 2 * maxTT)
                    {
                        maxShear = s;
                        maxTT = t;
                    }
                }

                // Calculate governing stirrup
                string governingStirrup = Algorithms.RebarCalculator.CalculateStirrup(
                    maxShear, maxTT, beamWidth, settings
                );

                sol.StirrupDesigns[span.SpanId + "_Stirrup_Governing"] = governingStirrup;
            }
        }

        private static double GetValueAt(double[] arr, int index)
        {
            if (arr == null || index >= arr.Length) return 0;
            return arr[index] > 0 ? arr[index] : 0;
        }
    }
}
