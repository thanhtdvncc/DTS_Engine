using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using DTS_Engine.Core.Data;
using DTS_Engine.Core.Utils;
using DTS_Engine.Core.Algorithms;
using Autodesk.AutoCAD.DatabaseServices;

namespace DTS_Engine.Core.Utils
{
    public static class ReportDataManager
    {
        public static string BuildReportJson(List<BeamGroup> groups)
        {
            if (groups == null || !groups.Any()) return "[]";

            // Dictionary để gộp theo tên tiết diện
            // Key: SectionName (VD: "300x500" hoặc tên do user đặt)
            var sectionMap = new Dictionary<string, ReportGroupData>();
            string projectName = string.IsNullOrEmpty(AcadUtils.Doc?.Name) ? "DTS Project" : System.IO.Path.GetFileNameWithoutExtension(AcadUtils.Doc.Name);

            foreach (var group in groups)
            {
                foreach (var span in group.Spans)
                {
                    // Lấy ID tiết diện: Ưu tiên nhãn tiết diện gán bởi người dùng, fallback về WxH
                    string secName = span.xSectionLabel;
                    if (string.IsNullOrEmpty(secName))
                    {
                        secName = $"{span.Width}x{span.Height}";
                    }

                    if (!sectionMap.ContainsKey(secName))
                    {
                        sectionMap[secName] = new ReportGroupData
                        {
                            GroupName = secName,      // Hiển thị tên tiết diện làm header
                            SectionName = secName,
                            ProjectName = projectName,
                            Spans = new List<ReportSpanData>()
                        };
                    }

                    var reportSpan = ConvertSpanToReportData(span);
                    sectionMap[secName].Spans.Add(reportSpan);
                }
            }

            // Chuyển sang danh sách để trả về
            var sortedGroups = sectionMap.Values.OrderBy(g => g.SectionName).ToList();

            return JsonConvert.SerializeObject(sortedGroups, Formatting.Indented);
        }

        private static ReportSpanData ConvertSpanToReportData(SpanData span)
        {
            var reportSpan = new ReportSpanData
            {
                SpanId = span.SpanId,
                Section = $"{span.Width}x{span.Height}",
                Length = Math.Round(span.Length * 1000).ToString(), // mm
                Material = "B25 / CB400" // Fallback hoặc lấy từ segment đầu tiên
            };

            // Lấy dữ liệu 3 vùng từ các PhysicalSegment
            // Chiến thuật: Lấy segment đầu cho Left, segment cuối cho Right, segment giữa cho Mid.
            // Hoặc nếu 1 segment duy nhất thì lấy 3 zone của nó.

            var firstSeg = span.Segments.FirstOrDefault();
            var lastSeg = span.Segments.LastOrDefault();
            var midSeg = span.Segments.ElementAtOrDefault(span.Segments.Count / 2);

            if (firstSeg != null)
            {
                var dataLeft = GetResultData(firstSeg.EntityHandle);
                var dataMid = GetResultData(midSeg.EntityHandle);
                var dataRight = GetResultData(lastSeg.EntityHandle);

                reportSpan.Material = $"{dataLeft?.ConcreteGrade ?? ""} / {dataLeft?.SteelGrade ?? ""}";

                reportSpan.Left = CreateStationData(dataLeft, 0, span.SpanId, "Gối Trái");
                reportSpan.Mid = CreateStationData(dataMid, 1, span.SpanId, "Giữa Nhịp");
                reportSpan.Right = CreateStationData(dataRight, 2, span.SpanId, "Gối Phải");
            }

            return reportSpan;
        }

        private static ReportStationData CreateStationData(BeamResultData data, int zoneIndex, string spanId, string stationLabel)
        {
            var station = new ReportStationData
            {
                ElementId = spanId,
                Station = stationLabel,
                LoadCase = data?.TopCombo?.ElementAtOrDefault(zoneIndex) ?? ""
            };

            string sapElem = data?.SapElementNos?.ElementAtOrDefault(zoneIndex) ?? "-";
            string sapLoc = data?.LocationMm?.ElementAtOrDefault(zoneIndex).ToString() ?? "-";

            // Top - mm2
            double topReq = (data?.TopArea?[zoneIndex] ?? 0) * 100.0;
            double topProv = (data?.TopAreaProv?[zoneIndex] ?? 0) * 100.0;
            station.TopResult = new ReportForceResult
            {
                ElementId = sapElem,
                Station = stationLabel,
                LocationMm = sapLoc,
                Moment = data != null ? Math.Round(data.TopMoment[zoneIndex], 2) : 0,
                AsCalc = Math.Round(topReq, 1),
                AsProv = Math.Round(topProv, 1),
                RebarStr = data?.TopRebarString?.ElementAtOrDefault(zoneIndex) ?? "-",
                LoadCase = data?.TopCombo?.ElementAtOrDefault(zoneIndex) ?? "-",
                Ratio = CalcRatio(topReq, topProv),
                Conclusion = CalcRatio(topReq, topProv) <= 1.05 ? "OK" : "NG"
            };

            // Bot - mm2
            double botReq = (data?.BotArea?[zoneIndex] ?? 0) * 100.0;
            double botProv = (data?.BotAreaProv?[zoneIndex] ?? 0) * 100.0;
            station.BotResult = new ReportForceResult
            {
                ElementId = sapElem,
                Station = stationLabel,
                LocationMm = sapLoc,
                Moment = data != null ? Math.Round(data.BotMoment[zoneIndex], 2) : 0,
                AsCalc = Math.Round(botReq, 1),
                AsProv = Math.Round(botProv, 1),
                RebarStr = data?.BotRebarString?.ElementAtOrDefault(zoneIndex) ?? "-",
                LoadCase = data?.BotCombo?.ElementAtOrDefault(zoneIndex) ?? "-",
                Ratio = CalcRatio(botReq, botProv),
                Conclusion = CalcRatio(botReq, botProv) <= 1.05 ? "OK" : "NG"
            };

            // Stirrup Total (2At/st + Av/sv) - mm2/mm
            double stirTotalReq = (data != null) ? (2 * data.TTArea[zoneIndex] + data.ShearArea[zoneIndex]) : 0;
            double stirProv = data != null ? (StirrupStringParser.ParseAsProv(data.StirrupString?.ElementAtOrDefault(zoneIndex)) / 100.0) : 0; // cm2/m -> mm2/mm (div 100)

            station.StirrupResult = new ReportForceResult
            {
                ElementId = sapElem,
                Station = stationLabel,
                LocationMm = sapLoc,
                Shear = data != null ? Math.Round(data.ShearForce[zoneIndex], 2) : 0,
                AsCalc = Math.Round(stirTotalReq, 3),
                AsProv = Math.Round(stirProv, 3),
                RebarStr = data?.StirrupString?.ElementAtOrDefault(zoneIndex) ?? "-",
                LoadCase = data?.ShearCombo?.ElementAtOrDefault(zoneIndex) ?? "-",
                Ratio = CalcRatio(stirTotalReq, stirProv),
                Conclusion = CalcRatio(stirTotalReq, stirProv) <= 1.05 ? "OK" : "NG"
            };

            // Stirrup Only (2At/st ?) - In spec 4.6 it says "Chỉ xét thép đai". 
            // Often this means the Torsion part or Shear part. 
            // According to spec 4.5 vs 4.6: 4.5 is TOTAL, 4.6 is "Only Stirrup".
            // If ttArea is At/s (torsion), then 2*ttArea is the stirrup part for torsion.
            double stirOnlyReq = (data != null) ? (2 * data.TTArea[zoneIndex]) : 0;
            station.StirrupOnlyResult = new ReportForceResult
            {
                ElementId = sapElem,
                Station = stationLabel,
                LocationMm = sapLoc,
                AsCalc = Math.Round(stirOnlyReq, 3),
                AsProv = Math.Round(stirProv, 3), // Still same rebar provided
                Ratio = CalcRatio(stirOnlyReq, stirProv),
                Conclusion = CalcRatio(stirOnlyReq, stirProv) <= 1.5 ? "OK" : "NG" // Higher limit for check
            };

            // Web Bar (Side bar) - mm2 (Total Area Al req)
            double alReq = (data?.TorsionArea?[zoneIndex] ?? 0) * 100.0;
            double webProv = data != null ? (RebarCalculator.ParseRebarArea(data.WebBarString?.ElementAtOrDefault(zoneIndex)) * 100.0) : 0;

            station.AlResult = new ReportForceResult
            {
                ElementId = sapElem,
                Station = stationLabel,
                LocationMm = sapLoc,
                AsCalc = Math.Round(alReq, 1),
                AsProv = Math.Round(webProv, 1),
                LoadCase = data?.TorsionCombo?.ElementAtOrDefault(zoneIndex) ?? "-",
                Ratio = CalcRatio(alReq, webProv),
                Conclusion = CalcRatio(alReq, webProv) <= 1.05 ? "OK" : "NG"
            };

            station.Legs = data != null ? StirrupStringParser.GetLegs(data.StirrupString?.ElementAtOrDefault(zoneIndex)) : 0; // No fallback to 2

            station.WebResult = new ReportForceResult
            {
                ElementId = sapElem,
                Station = stationLabel,
                RebarStr = data?.WebBarString?.ElementAtOrDefault(zoneIndex) ?? "-",
                AsProv = Math.Round(webProv, 1)
            };

            return station;
        }

        private static double CheckStirrupReq(BeamResultData data, int idx)
        {
            if (data == null) return 0;
            // Công thức: 2*At/s + Av/s. SAP returns in cm2/cm -> * 100 for cm2/m
            return (2 * data.TTArea[idx] + data.ShearArea[idx]) * 100.0;
        }

        private static double CalcRatio(double req, double prov)
        {
            if (prov <= 1e-6) return req > 1e-6 ? 9.99 : 0;
            return Math.Round(req / prov, 3);
        }

        private static BeamResultData GetResultData(string handle)
        {
            if (string.IsNullOrEmpty(handle)) return null;
            try
            {
                using (var tr = HostApplicationServices.WorkingDatabase.TransactionManager.StartTransaction())
                {
                    ObjectId id = AcadUtils.GetObjectIdFromHandle(handle);
                    if (!id.IsNull)
                    {
                        using (var obj = tr.GetObject(id, OpenMode.ForRead))
                        {
                            return XDataUtils.ReadElementData<BeamResultData>(obj);
                        }
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
