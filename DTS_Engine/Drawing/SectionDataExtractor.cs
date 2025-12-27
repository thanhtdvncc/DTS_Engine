using DTS_Engine.Core.Data;
using DTS_Engine.Core.Utils;
using DTS_Engine.Drawing.Models;
using System.Collections.Generic;
using System.Linq;

namespace DTS_Engine.Drawing
{
    /// <summary>
    /// Trích xuất và chuyển đổi dữ liệu từ BeamResultData sang Drawing Models.
    /// </summary>
    public class SectionDataExtractor
    {
        public BeamScheduleRowData Extract(BeamResultData beamData, double cover = 25.0)
        {
            var row = new BeamScheduleRowData
            {
                BeamName = beamData.BeamName ?? beamData.SectionLabel,
                SizeLabel = $"{(int)(beamData.Width * 10)}x{(int)(beamData.SectionHeight * 10)}"
            };

            // BeamResultData.TopRS/BotRS/StirRS/WebRS có độ dài 3 (0=Start, 1=Mid, 2=End)
            row.Cells.Add(CreateCell(beamData, 0, "LEFT", cover));
            row.Cells.Add(CreateCell(beamData, 1, "MID", cover));
            row.Cells.Add(CreateCell(beamData, 2, "RIGHT", cover));

            return row;
        }

        private SectionCellData CreateCell(BeamResultData beam, int index, string locationName, double cover)
        {
            var cell = new SectionCellData
            {
                LocationName = locationName,
                DataIndex = index,
                Width = beam.Width * 10.0,
                Height = beam.SectionHeight * 10.0,
                Cover = cover,

                TopText = (beam.TopRS != null && beam.TopRS.Length > index) ? beam.TopRS[index] : "",
                BotText = (beam.BotRS != null && beam.BotRS.Length > index) ? beam.BotRS[index] : "",
                StirrupText = (beam.StirRS != null && beam.StirRS.Length > index) ? beam.StirRS[index] : "",
                WebText = (beam.WebRS != null && beam.WebRS.Length > index) ? beam.WebRS[index] : "-"
            };

            // Parse chi tiết thép để vẽ
            cell.TopLayers = ParseLayers(cell.TopText);
            cell.BotLayers = ParseLayers(cell.BotText);
            cell.Stirrup = ParseStirrup(cell.StirrupText);

            return cell;
        }

        private List<RebarLayer> ParseLayers(string rebarStr)
        {
            var details = RebarStringParser.GetDetails(rebarStr);
            return details.Select(d => new RebarLayer
            {
                Count = d.Count,
                Diameter = d.Diameter,
                Area = d.Area
            }).ToList();
        }

        private StirrupInfo ParseStirrup(string stirrupStr)
        {
            if (string.IsNullOrWhiteSpace(stirrupStr)) return null;

            int legs = StirrupStringParser.GetLegs(stirrupStr);

            // Sử dụng Regex từ StirrupStringParser để parse đường kính và khoảng cách
            // Format phổ biến: d8@150
            var match = System.Text.RegularExpressions.Regex.Match(stirrupStr, @"(\d*)\s*[-]?\s*[dDfF]?[phi|fi|Ø]?\s*(\d+)\s*[@asAS]\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (match.Success)
            {
                int.TryParse(match.Groups[2].Value, out int diameter);
                int.TryParse(match.Groups[3].Value, out int spacing);

                return new StirrupInfo
                {
                    Diameter = diameter == 0 ? 8 : diameter,
                    Spacing = spacing == 0 ? 150 : spacing,
                    Legs = legs == 0 ? 2 : legs
                };
            }

            return new StirrupInfo { Diameter = 8, Spacing = 150, Legs = 2 };
        }
    }
}
