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

            var sectionMap = new Dictionary<string, ReportGroupData>();
            string projectName = string.IsNullOrEmpty(AcadUtils.Doc?.Name) ? "DTS Project" : System.IO.Path.GetFileNameWithoutExtension(AcadUtils.Doc.Name);

            foreach (var group in groups)
            {
                foreach (var span in group.Spans)
                {
                    // [FIX] Gom toàn bộ dầm vào một nhóm duy nhất để hiện danh sách tab tập trung
                    string key = "ALL_SECTIONS";

                    if (!sectionMap.ContainsKey(key))
                    {
                        sectionMap[key] = new ReportGroupData
                        {
                            GroupName = "Thuyết minh tính toán",
                            SectionName = "Toàn bộ dầm",
                            ProjectName = projectName,
                            Spans = new List<ReportSpanData>()
                        };
                    }

                    var reportSpan = ConvertSpanToReportData(span, group.GroupName);
                    sectionMap[key].Spans.Add(reportSpan);
                }
            }

            var sortedGroups = sectionMap.Values.ToList();
            return JsonConvert.SerializeObject(sortedGroups, Formatting.Indented);
        }

        private static ReportSpanData ConvertSpanToReportData(SpanData span, string beamName)
        {
            var reportSpan = new ReportSpanData
            {
                // [FIX] Hiển thị tên dầm kèm số hiệu nhịp trong Tab
                SpanId = string.IsNullOrEmpty(beamName) ? span.SpanId : $"{beamName} - {span.SpanId}",
                Section = $"{span.Width}x{span.Height}",
                Length = Math.Round(span.Length * 1000).ToString(), // mm
                Material = "B25 / CB400"
            };

            var firstSeg = span.Segments.FirstOrDefault();
            var lastSeg = span.Segments.LastOrDefault();
            var midSeg = span.Segments.ElementAtOrDefault(span.Segments.Count / 2);

            if (firstSeg != null)
            {
                var (dataL, optL) = GetAllData(firstSeg.EntityHandle);
                var (dataM, optM) = GetAllData(midSeg.EntityHandle);
                var (dataR, optR) = GetAllData(lastSeg.EntityHandle);

                reportSpan.Material = $"{dataL?.ConcreteGrade ?? "B25"} / {dataL?.SteelGrade ?? "CB400"}";

                reportSpan.Left = CreateStationData(dataL, optL, 0, span.SpanId, "L1");
                reportSpan.Mid = CreateStationData(dataM, optM, 1, span.SpanId, "Center");
                reportSpan.Right = CreateStationData(dataR, optR, 2, span.SpanId, "L2");
            }

            return reportSpan;
        }

        private static ReportStationData CreateStationData(BeamResultData data, XDataUtils.RebarOptionData opt, int zoneIndex, string spanId, string stationLabel)
        {
            var settings = DtsSettings.Instance.Beam;
            var station = new ReportStationData
            {
                ElementId = spanId,
                Station = stationLabel,
                LoadCase = data?.TopCombo?.ElementAtOrDefault(zoneIndex) ?? "-"
            };

            string sapElem = data?.SapElementNos?.ElementAtOrDefault(zoneIndex) ?? "-";
            string sapLoc = data?.LocationMm?.ElementAtOrDefault(zoneIndex).ToString() ?? "-";

            // Top - mm2 (Flexure + Torsion)
            double? topReqCm2 = data?.TopArea?[zoneIndex];
            double? topTorCm2 = data?.TorsionArea?[zoneIndex] * settings.TorsionDist_TopBar;
            double? topReqTotal = (topReqCm2 + topTorCm2) * 100.0;

            double? topProv = opt != null ? opt.GetAreaProv(zoneIndex, true) * 100.0 : (double?)null;
            string topRebarStr = opt != null ? opt.GetCombinedRebarString(zoneIndex, true) : "-";

            station.TopResult = new ReportForceResult
            {
                ElementId = $"{sapElem} ({data?.TopCombo?.ElementAtOrDefault(zoneIndex) ?? "-"})",
                Station = stationLabel,
                LocationMm = sapLoc,
                AsCalc = topReqTotal.HasValue ? Math.Round(topReqTotal.Value, 1) : (double?)null,
                AsProv = topProv.HasValue ? Math.Round(topProv.Value, 1) : (double?)null,
                RebarStr = topRebarStr,
                LoadCase = data?.TopCombo?.ElementAtOrDefault(zoneIndex) ?? "-",
                Ratio = CalcRatio(topReqTotal, topProv),
                Conclusion = GetConclusion(CalcRatio(topReqTotal, topProv))
            };

            // Bot - mm2 (Flexure + Torsion)
            double? botReqCm2 = data?.BotArea?[zoneIndex];
            double? botTorCm2 = data?.TorsionArea?[zoneIndex] * settings.TorsionDist_BotBar;
            double? botReqTotal = (botReqCm2 + botTorCm2) * 100.0;

            double? botProv = opt != null ? opt.GetAreaProv(zoneIndex, false) * 100.0 : (double?)null;
            string botRebarStr = opt != null ? opt.GetCombinedRebarString(zoneIndex, false) : "-";

            station.BotResult = new ReportForceResult
            {
                ElementId = $"{sapElem} ({data?.BotCombo?.ElementAtOrDefault(zoneIndex) ?? "-"})",
                Station = stationLabel,
                LocationMm = sapLoc,
                AsCalc = botReqTotal.HasValue ? Math.Round(botReqTotal.Value, 1) : (double?)null,
                AsProv = botProv.HasValue ? Math.Round(botProv.Value, 1) : (double?)null,
                RebarStr = botRebarStr,
                LoadCase = data?.BotCombo?.ElementAtOrDefault(zoneIndex) ?? "-",
                Ratio = CalcRatio(botReqTotal, botProv),
                Conclusion = GetConclusion(CalcRatio(botReqTotal, botProv))
            };

            // Stirrup Total (2At/st + Av/sv) - mm2/mm
            double? stirTotalReq = (data != null) ? (2 * data.TTArea[zoneIndex] + data.ShearArea[zoneIndex]) * 10.0 : (double?)null;
            string stirrupStr = opt?.GetStirrupAt(zoneIndex) ?? "-";
            double? stirProv = !string.IsNullOrEmpty(stirrupStr) && stirrupStr != "-" ? (RebarCalculator.ParseStirrupAreaPerLen(stirrupStr) * 10.0) : (double?)null;

            station.StirrupResult = new ReportForceResult
            {
                ElementId = $"{sapElem} ({data?.ShearCombo?.ElementAtOrDefault(zoneIndex) ?? "-"})",
                Station = stationLabel,
                LocationMm = sapLoc,
                AsCalc = stirTotalReq.HasValue ? Math.Round(stirTotalReq.Value, 3) : (double?)null,
                AsProv = stirProv.HasValue ? Math.Round(stirProv.Value, 3) : (double?)null,
                RebarStr = stirrupStr,
                LoadCase = data?.ShearCombo?.ElementAtOrDefault(zoneIndex) ?? "-",
                Ratio = CalcRatio(stirTotalReq, stirProv),
                Conclusion = GetConclusion(CalcRatio(stirTotalReq, stirProv))
            };

            // Stirrup Only (2At/sv) - mm2/mm
            double? stirOnlyReq = (data != null) ? (2 * data.TTArea[zoneIndex]) * 10.0 : (double?)null;
            station.StirrupOnlyResult = new ReportForceResult
            {
                ElementId = $"{sapElem} ({data?.TorsionCombo?.ElementAtOrDefault(zoneIndex) ?? "-"})", // Use Torsion Combo
                Station = stationLabel,
                LocationMm = sapLoc,
                AsCalc = stirOnlyReq.HasValue ? Math.Round(stirOnlyReq.Value, 3) : (double?)null,
                LoadCase = data?.TorsionCombo?.ElementAtOrDefault(zoneIndex) ?? "-",
                Ratio = CalcRatio(stirOnlyReq, stirProv),
                Conclusion = GetConclusion(CalcRatio(stirOnlyReq, stirProv))
            };

            // Shear Only (Av/sv) - mm2/mm
            double? vAreaReq = (data != null) ? (data.ShearArea[zoneIndex]) * 10.0 : (double?)null;
            station.ShearResult = new ReportForceResult
            {
                ElementId = $"{sapElem} ({data?.ShearCombo?.ElementAtOrDefault(zoneIndex) ?? "-"})",
                Station = stationLabel,
                AsCalc = vAreaReq.HasValue ? Math.Round(vAreaReq.Value, 3) : (double?)null,
                LoadCase = data?.ShearCombo?.ElementAtOrDefault(zoneIndex) ?? "-"
            };

            // Web Bar (Side bar) - mm2 (Total Area Al req * SideBarDist)
            double? alSideReq = data != null ? (data.TorsionArea?[zoneIndex] * settings.TorsionDist_SideBar * 100.0) : (double?)null;
            string webStr = opt?.GetWebAt(zoneIndex) ?? "-";
            double? webProvArea = !string.IsNullOrEmpty(webStr) && webStr != "-" ? (RebarCalculator.ParseRebarArea(webStr) * 100.0) : (double?)null;

            station.AlResult = new ReportForceResult
            {
                ElementId = data?.TorsionSapNo?.ElementAtOrDefault(zoneIndex) ?? data?.SapElementNos?.ElementAtOrDefault(zoneIndex) ?? "-",
                Station = stationLabel,
                LocationMm = (data?.TorsionLocMm?.ElementAtOrDefault(zoneIndex) ?? data?.LocationMm?.ElementAtOrDefault(zoneIndex))?.ToString() ?? "-",
                AsCalc = alSideReq.HasValue ? Math.Round(alSideReq.Value, 1) : (double?)null,
                AsProv = webProvArea.HasValue ? Math.Round(webProvArea.Value, 1) : (double?)null,
                LoadCase = data?.TorsionCombo?.ElementAtOrDefault(zoneIndex) ?? "-",
                Ratio = CalcRatio(alSideReq, webProvArea),
                Conclusion = GetConclusion(CalcRatio(alSideReq, webProvArea))
            };

            station.Legs = opt != null ? StirrupStringParser.GetLegs(opt.GetStirrupAt(zoneIndex)) : 0;

            station.WebResult = new ReportForceResult
            {
                ElementId = data?.TorsionSapNo?.ElementAtOrDefault(zoneIndex) ?? data?.SapElementNos?.ElementAtOrDefault(zoneIndex) ?? "-",
                Station = stationLabel,
                LocationMm = (data?.TorsionLocMm?.ElementAtOrDefault(zoneIndex) ?? data?.LocationMm?.ElementAtOrDefault(zoneIndex))?.ToString() ?? "-",
                RebarStr = webStr,
                AsProv = webProvArea.HasValue ? Math.Round(webProvArea.Value, 1) : (double?)null
            };

            return station;
        }

        private static string GetConclusion(double? ratio, double threshold = 0.99)
        {
            if (!ratio.HasValue) return "-";
            return ratio.Value >= threshold ? "OK" : "NG";
        }

        private static double? CalcRatio(double? req, double? prov)
        {
            if (!req.HasValue || !prov.HasValue) return null;
            if (req.Value <= 1e-6) return 9.99;
            if (prov.Value <= 1e-6) return 0;
            return Math.Round(prov.Value / req.Value, 3);
        }

        private static (BeamResultData Result, XDataUtils.RebarOptionData User) GetAllData(string handle)
        {
            if (string.IsNullOrEmpty(handle)) return (null, null);
            try
            {
                using (var tr = HostApplicationServices.WorkingDatabase.TransactionManager.StartTransaction())
                {
                    ObjectId id = AcadUtils.GetObjectIdFromHandle(handle);
                    if (!id.IsNull)
                    {
                        using (var obj = tr.GetObject(id, OpenMode.ForRead))
                        {
                            var result = XDataUtils.ReadRebarData(obj);
                            var optUser = XDataUtils.ReadOptUser(obj);
                            return (result, optUser);
                        }
                    }
                }
            }
            catch { }
            return (null, null);
        }
    }
}
