using ClosedXML.Excel;
using DTS_Engine.Core.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DTS_Engine.Core.Utils
{
    public class CalculationReportExcelGenerator
    {
        private static readonly XLColor HEADER_BG = XLColor.FromArgb(241, 243, 245);
        private static readonly XLColor SAFE_COLOR = XLColor.FromArgb(232, 250, 232); // Light Green
        private static readonly XLColor UNSAFE_COLOR = XLColor.FromArgb(255, 235, 235); // Light Red
        private static readonly XLColor REBAR_FILL = XLColor.FromArgb(255, 249, 219); // Light Yellow

        public static string Generate(ReportGroupData groupData, string outputPath = null, bool isSimple = false)
        {
            if (groupData == null) return null;

            using (var workbook = new XLWorkbook())
            {
                var ws = workbook.Worksheets.Add("DTS Report");

                int row = 1;
                foreach (var span in groupData.Spans)
                {
                    if (isSimple)
                        row = WriteSpanReduced(ws, span, row);
                    else
                        row = WriteSpanDetailed(ws, span, row);

                    row += 2; // Spacing between spans
                }

                // Finalize sheet styling
                ws.Columns().AdjustToContents();
                ws.Column(1).Width = 12; // Group
                ws.Column(2).Width = 20; // Item
                ws.Column(3).Width = 18; // Sub
                if (isSimple)
                {
                    ws.Column(4).Width = 15; // End
                    ws.Column(5).Width = 15; // Center
                    ws.Column(6).Width = 12; // Result merged
                    ws.Column(7).Width = 12;
                }
                else
                {
                    ws.Columns(4, 6).Width = 15; // L1, Mid, L2
                    ws.Column(7).Width = 12; // Result
                }

                if (string.IsNullOrEmpty(outputPath))
                {
                    string tempFolder = Path.GetTempPath();
                    string tag = isSimple ? "Reduced" : "Full";
                    string safeGroupName = SanitizeFileName(groupData.GroupName);
                    outputPath = Path.Combine(tempFolder, $"DTS_Report_{tag}_{safeGroupName}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
                }

                workbook.SaveAs(outputPath);
                return outputPath;
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Unnamed";
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        private static int WriteSpanDetailed(IXLWorksheet ws, ReportSpanData span, int row)
        {
            int startRow = row;

            // Header dầm
            var header = ws.Range(row, 4, row, 6).Merge();
            header.Value = $"{span.SpanId} (RC{span.Section})";
            header.Style.Font.Bold = true;
            header.Style.Font.FontSize = 14;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            row++;

            // Location Header
            ws.Cell(row, 2).Value = "Location";
            ws.Cell(row, 4).Value = "L1 (Left)";
            ws.Cell(row, 5).Value = "Center";
            ws.Cell(row, 6).Value = "L2 (Right)";
            ws.Range(row, 2, row, 6).Style.Font.Bold = true;
            ws.Range(row, 4, row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            row++;

            // BY SAP2000
            ws.Range(row, 1, row + 11, 1).Merge().Value = "By SAP2000";
            StyleBlockLabel(ws.Cell(row, 1));

            row = WriteDtlRow(ws, row, "As, top (mm²)", span, s => s.TopResult, false);
            row = WriteDtlRow(ws, row, "As, bot (mm²)", span, s => s.BotResult, false);

            // Shear demand
            row = WriteDtlForce(ws, row, "2At/st + Av/sv", "mm²/mm (Total)", span, s => s.StirrupResult.AsCalc, s => s.StirrupResult.LoadCase);
            row = WriteDtlForce(ws, row, "2At/sv", "mm²/mm (Stirrup)", span, s => s.StirrupOnlyResult.AsCalc, s => s.StirrupOnlyResult.LoadCase);
            row = WriteDtlForce(ws, row, "Al (mm²)", "Torsion Long.", span, s => s.AlResult.AsCalc, s => s.AlResult.LoadCase);

            // FINAL USE
            ws.Range(row, 1, row + 14, 1).Merge().Value = "Final Use";
            StyleBlockLabel(ws.Cell(row, 1));

            row = WriteDtlFinal(ws, row, "Top bar", span, s => s.TopResult);
            row = WriteDtlFinal(ws, row, "Bottom bar", span, s => s.BotResult);

            // Stirrup
            ws.Cell(row, 2).Value = "Stirrup";
            ws.Cell(row, 3).Value = "No. Leg";
            ws.Cell(row, 4).Value = span.Left.Legs;
            ws.Cell(row, 5).Value = span.Mid.Legs;
            ws.Cell(row, 6).Value = span.Right.Legs;
            ws.Cell(row, 7).Value = "Don";
            ws.Range(row, 2, row, 7).Style.Font.Bold = true;
            row++;

            ws.Cell(row, 3).Value = "Rebar config";
            ws.Cell(row, 4).Value = span.Left.StirrupResult.RebarStr;
            ws.Cell(row, 5).Value = span.Mid.StirrupResult.RebarStr;
            ws.Cell(row, 6).Value = span.Right.StirrupResult.RebarStr;
            ws.Range(row, 4, row, 6).Style.Fill.BackgroundColor = REBAR_FILL;
            row++;

            row = WriteDtlCheck(ws, row, "2At/st+Av/sv (Prv)", span, s => s.StirrupResult.AsProv, s => s.StirrupResult.Conclusion, s => s.StirrupResult.AsCalc);
            row = WriteDtlCheck(ws, row, "2At/sv (Prv)", span, s => s.StirrupOnlyResult.AsProv, s => s.StirrupOnlyResult.Conclusion, s => s.StirrupOnlyResult.AsCalc);

            // Web
            ws.Cell(row, 2).Value = "Web bar";
            ws.Cell(row, 3).Value = "Rebar config";
            ws.Cell(row, 4).Value = span.Left.WebResult.RebarStr;
            ws.Cell(row, 5).Value = span.Mid.WebResult.RebarStr;
            ws.Cell(row, 6).Value = span.Right.WebResult.RebarStr;
            ws.Range(row, 4, row, 6).Style.Fill.BackgroundColor = REBAR_FILL;
            ApplyResult(ws.Cell(row, 7), span.Left.AlResult.Conclusion);
            row++;
            ws.Cell(row, 3).Value = "Provide (mm²)";
            ws.Cell(row, 4).Value = span.Left.WebResult.AsProv;
            ws.Cell(row, 7).Value = span.Left.AlResult.AsCalc; // Reference
            row++;

            // Result Check
            var finalStatus = (span.Left.TopResult.Conclusion == "OK" && span.Left.BotResult.Conclusion == "OK" && span.Left.StirrupResult.Conclusion == "OK" && span.Left.AlResult.Conclusion == "OK") ? "OK" : "NG";
            ws.Cell(row, 2).Value = "RESULT CHECK";
            ws.Range(row, 2, row, 6).Merge().Value = "ALL REQUIREMENTS SATISFIED";
            ws.Range(row, 2, row, 6).Style.Font.Bold = true;
            ws.Range(row, 2, row, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ApplyResult(ws.Cell(row, 7), finalStatus);
            row++;

            ApplyBorders(ws, startRow, row - 1, 7);
            return row;
        }

        private static int WriteSpanReduced(IXLWorksheet ws, ReportSpanData span, int row)
        {
            int startRow = row;

            // Identity Anchor
            var header = ws.Range(row, 4, row, 5).Merge();
            header.Value = $"{span.SpanId} (RC{span.Section})";
            header.Style.Font.Bold = true;
            header.Style.Font.FontSize = 14;
            header.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            row++;

            // Location Row
            ws.Cell(row, 2).Value = "Location";
            ws.Cell(row, 4).Value = "End";
            ws.Cell(row, 5).Value = "Center";
            ws.Range(row, 2, row, 5).Style.Font.Bold = true;
            ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            row++;

            // BY SAP2000
            ws.Range(row, 1, row + 11, 1).Merge().Value = "By SAP2000";
            StyleBlockLabel(ws.Cell(row, 1));

            row = WriteRedRow(ws, row, "As, top (mm²)", span, s => s.TopResult);
            row = WriteRedRow(ws, row, "As, bot (mm²)", span, s => s.BotResult);

            // Shear
            row = WriteRedForce(ws, row, "2At/st + Av/sv", "mm²/mm (Total)", span, s => s.StirrupResult.AsCalc, s => s.StirrupResult.LoadCase);
            row = WriteRedForce(ws, row, "2At/sv", "mm²/mm (Stirrup)", span, s => s.StirrupOnlyResult.AsCalc, s => s.StirrupOnlyResult.LoadCase);
            row = WriteRedForce(ws, row, "Al (mm²)", "Torsion Long.", span, s => s.AlResult.AsCalc, s => s.AlResult.LoadCase);

            // FINAL USE
            ws.Range(row, 1, row + 15, 1).Merge().Value = "Final Use";
            StyleBlockLabel(ws.Cell(row, 1));

            row = WriteRedFinal(ws, row, "Top bar", span, s => s.TopResult);
            row = WriteRedFinal(ws, row, "Bottom bar", span, s => s.BotResult);

            // Stirrup
            ws.Cell(row, 2).Value = "Stirrup";
            ws.Cell(row, 3).Value = "No. Leg";
            ws.Cell(row, 4).Value = Math.Max(span.Left.Legs, span.Right.Legs);
            ws.Cell(row, 5).Value = span.Mid.Legs;
            ws.Range(row, 6, row, 7).Merge().Value = "Don";
            ws.Range(row, 6, row, 7).Style.Font.Bold = true;
            row++;

            ws.Cell(row, 3).Value = "Rebar";
            ws.Cell(row, 4).Value = span.Left.StirrupResult.RebarStr; // End typically uses L1 config
            ws.Cell(row, 5).Value = span.Mid.StirrupResult.RebarStr;
            ws.Range(row, 4, row, 5).Style.Fill.BackgroundColor = REBAR_FILL;
            row++;

            row = WriteRedCheck(ws, row, "2At/st+Av/sv", "Total", span, s => s.StirrupResult.AsProv, s => s.StirrupResult.Conclusion, s => s.StirrupResult.AsCalc);
            row = WriteRedCheck(ws, row, "2At/sv", "Stirrup", span, s => s.StirrupOnlyResult.AsProv, s => s.StirrupOnlyResult.Conclusion, s => s.StirrupOnlyResult.AsCalc);

            // Web Bar
            ws.Cell(row, 2).Value = "Web bar";
            ws.Range(row, 4, row, 5).Merge().Value = span.Left.WebResult.RebarStr;
            ws.Range(row, 4, row, 5).Style.Fill.BackgroundColor = REBAR_FILL;
            ws.Range(row, 6, row, 7).Merge();
            ApplyResult(ws.Cell(row, 6), span.Left.AlResult.Conclusion);
            row++;
            ws.Cell(row, 3).Value = "Provide (mm²)";
            ws.Range(row, 4, row, 5).Merge().Value = span.Left.WebResult.AsProv;
            ws.Range(row, 6, row, 7).Merge().Value = span.Left.AlResult.AsCalc; // Reference
            row++;

            // Result Check
            var finalStatus = (span.Left.TopResult.Conclusion == "OK" && span.Left.BotResult.Conclusion == "OK" && span.Left.StirrupResult.Conclusion == "OK" && span.Left.AlResult.Conclusion == "OK") ? "OK" : "NG";
            ws.Range(row, 2, row, 5).Merge().Value = "RESULT CHECK";
            ws.Range(row, 2, row, 5).Style.Font.Bold = true;
            ws.Range(row, 2, row, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            ws.Range(row, 6, row, 7).Merge();
            ApplyResult(ws.Cell(row, 6), finalStatus);
            row++;

            ApplyBorders(ws, startRow, row - 1, 7);
            return row;
        }

        // --- HELPERS DETAILED ---
        // --- HELPERS DETAILED ---
        private static int WriteDtlRow(IXLWorksheet ws, int row, string item, ReportSpanData span, Func<ReportStationData, ReportForceResult> sel, bool fill)
        {
            ws.Cell(row, 2).Value = item;
            ws.Cell(row, 3).Value = "Demand req.";
            SetCellValue(ws.Cell(row, 4), sel(span.Left).AsCalc);
            SetCellValue(ws.Cell(row, 5), sel(span.Mid).AsCalc);
            SetCellValue(ws.Cell(row, 6), sel(span.Right).AsCalc);
            row++;
            ws.Cell(row, 3).Value = "Element No. (LC)";
            ws.Cell(row, 4).Value = sel(span.Left).ElementId + " (" + sel(span.Left).LoadCase + ")";
            ws.Cell(row, 5).Value = sel(span.Mid).ElementId + " (" + sel(span.Mid).LoadCase + ")";
            ws.Cell(row, 6).Value = sel(span.Right).ElementId + " (" + sel(span.Right).LoadCase + ")";
            row++;
            ws.Cell(row, 3).Value = "Location (mm)";
            ws.Cell(row, 4).Value = sel(span.Left).LocationMm;
            ws.Cell(row, 5).Value = sel(span.Mid).LocationMm;
            ws.Cell(row, 6).Value = sel(span.Right).LocationMm;
            row++;
            return row;
        }

        private static int WriteDtlForce(IXLWorksheet ws, int row, string item, string sub, ReportSpanData span, Func<ReportStationData, double?> valSel, Func<ReportStationData, string> comboSel)
        {
            ws.Cell(row, 2).Value = item;
            ws.Cell(row, 3).Value = sub;
            SetCellValue(ws.Cell(row, 4), valSel(span.Left));
            SetCellValue(ws.Cell(row, 5), valSel(span.Mid));
            SetCellValue(ws.Cell(row, 6), valSel(span.Right));
            row++;
            ws.Cell(row, 3).Value = "Element (LC)";
            ws.Cell(row, 4).Value = comboSel(span.Left);
            ws.Cell(row, 5).Value = comboSel(span.Mid);
            ws.Cell(row, 6).Value = comboSel(span.Right);
            row++;
            return row;
        }

        private static int WriteDtlFinal(IXLWorksheet ws, int row, string item, ReportSpanData span, Func<ReportStationData, ReportForceResult> sel)
        {
            ws.Cell(row, 2).Value = item;
            ws.Cell(row, 3).Value = "Rebar config";
            ws.Cell(row, 4).Value = sel(span.Left).RebarStr;
            ws.Cell(row, 5).Value = sel(span.Mid).RebarStr;
            ws.Cell(row, 6).Value = sel(span.Right).RebarStr;
            ws.Range(row, 4, row, 6).Style.Fill.BackgroundColor = REBAR_FILL;
            ApplyResult(ws.Cell(row, 7), sel(span.Left).Conclusion);
            row++;
            ws.Cell(row, 3).Value = "Provide (mm²)";
            SetCellValue(ws.Cell(row, 4), sel(span.Left).AsProv);
            SetCellValue(ws.Cell(row, 5), sel(span.Mid).AsProv);
            SetCellValue(ws.Cell(row, 6), sel(span.Right).AsProv);
            row++;
            ws.Cell(row, 3).Value = "Ratio / Check";
            SetCellValue(ws.Cell(row, 4), sel(span.Left).Ratio);
            SetCellValue(ws.Cell(row, 5), sel(span.Mid).Ratio);
            SetCellValue(ws.Cell(row, 6), sel(span.Right).Ratio);
            SetCellValue(ws.Cell(row, 7), sel(span.Left).AsCalc); // Reference
            row++;
            return row;
        }

        private static int WriteDtlCheck(IXLWorksheet ws, int row, string sub, ReportSpanData span, Func<ReportStationData, double?> prvSel, Func<ReportStationData, string> conSel, Func<ReportStationData, double?> refSel)
        {
            ws.Cell(row, 3).Value = sub;
            SetCellValue(ws.Cell(row, 4), prvSel(span.Left));
            SetCellValue(ws.Cell(row, 5), prvSel(span.Mid));
            SetCellValue(ws.Cell(row, 6), prvSel(span.Right));
            ApplyResult(ws.Cell(row, 7), conSel(span.Left));
            row++;
            ws.Cell(row, 3).Value = "Ref SAP demand";
            SetCellValue(ws.Cell(row, 4), refSel(span.Left));
            SetCellValue(ws.Cell(row, 5), refSel(span.Mid));
            SetCellValue(ws.Cell(row, 6), refSel(span.Right));
            row++;
            return row;
        }

        // --- HELPERS REDUCED ---
        private static int WriteRedRow(IXLWorksheet ws, int row, string item, ReportSpanData span, Func<ReportStationData, ReportForceResult> sel)
        {
            ws.Cell(row, 2).Value = item;
            ws.Cell(row, 3).Value = "Demand req.";
            double vLeft = sel(span.Left).AsCalc ?? 0;
            double vRight = sel(span.Right).AsCalc ?? 0;
            ws.Range(row, 4, row, 5).Merge().Value = Math.Max(vLeft, vRight);
            row++;
            ws.Cell(row, 3).Value = "Element No. (LC)";
            ws.Range(row, 4, row, 5).Merge().Value = sel(span.Left).ElementId + " (" + sel(span.Left).LoadCase + ")";
            row++;
            ws.Cell(row, 3).Value = "Location (mm)";
            ws.Range(row, 4, row, 5).Merge().Value = sel(span.Left).LocationMm + " / " + sel(span.Right).LocationMm;
            row++;
            return row;
        }

        private static int WriteRedForce(IXLWorksheet ws, int row, string item, string sub, ReportSpanData span, Func<ReportStationData, double?> valSel, Func<ReportStationData, string> comboSel)
        {
            ws.Cell(row, 2).Value = item;
            ws.Cell(row, 3).Value = sub;
            double vLeft = valSel(span.Left) ?? 0;
            double vRight = valSel(span.Right) ?? 0;
            ws.Cell(row, 4).Value = Math.Max(vLeft, vRight);
            SetCellValue(ws.Cell(row, 5), valSel(span.Mid));
            row++;
            ws.Cell(row, 3).Value = "Element (LC)";
            ws.Range(row, 4, row, 5).Merge().Value = comboSel(span.Left);
            row++;
            return row;
        }

        private static int WriteRedFinal(IXLWorksheet ws, int row, string item, ReportSpanData span, Func<ReportStationData, ReportForceResult> sel)
        {
            ws.Cell(row, 2).Value = item;
            ws.Cell(row, 3).Value = "Rebar config";
            ws.Cell(row, 4).Value = sel(span.Left).RebarStr;
            ws.Cell(row, 5).Value = sel(span.Mid).RebarStr;
            ws.Range(row, 4, row, 5).Style.Fill.BackgroundColor = REBAR_FILL;
            ws.Range(row, 6, row, 7).Merge();
            ApplyResult(ws.Cell(row, 6), sel(span.Left).Conclusion);
            row++;
            ws.Cell(row, 3).Value = "Provide (mm²)";
            ws.Range(row, 4, row, 5).Merge().Value = sel(span.Left).AsProv;
            double vLeft = sel(span.Left).AsCalc ?? 0;
            double vRight = sel(span.Right).AsCalc ?? 0;
            ws.Range(row, 6, row, 7).Merge().Value = Math.Max(vLeft, vRight);
            row++;
            return row;
        }

        private static int WriteRedCheck(IXLWorksheet ws, int row, string item, string sub, ReportSpanData span, Func<ReportStationData, double?> prvSel, Func<ReportStationData, string> conSel, Func<ReportStationData, double?> refSel)
        {
            ws.Cell(row, 2).Value = item;
            ws.Cell(row, 3).Value = sub;
            SetCellValue(ws.Cell(row, 4), prvSel(span.Left));
            SetCellValue(ws.Cell(row, 5), prvSel(span.Mid));
            ws.Range(row, 6, row, 7).Merge();
            ApplyResult(ws.Cell(row, 6), conSel(span.Left));
            row++;
            ws.Cell(row, 3).Value = "SAP demand";
            double vLeft = refSel(span.Left) ?? 0;
            double vRight = refSel(span.Right) ?? 0;
            ws.Cell(row, 4).Value = Math.Max(vLeft, vRight);
            SetCellValue(ws.Cell(row, 5), refSel(span.Mid));
            row++;
            return row;
        }

        // --- COMMON ---
        private static void SetCellValue(IXLCell cell, double? val)
        {
            if (val.HasValue) cell.Value = val.Value;
            else cell.Value = "-";
        }

        private static void StyleBlockLabel(IXLCell cell)
        {
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HEADER_BG;
            // Optionally rotate text if needed: cell.Style.Alignment.TextRotation = 90;
        }

        private static void ApplyResult(IXLCell cell, string conclusion)
        {
            cell.Value = conclusion;
            cell.Style.Font.Bold = true;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            if (conclusion == "OK") cell.Style.Fill.BackgroundColor = SAFE_COLOR;
            else if (conclusion == "NG") cell.Style.Fill.BackgroundColor = UNSAFE_COLOR;
        }

        private static void ApplyBorders(IXLWorksheet ws, int r1, int r2, int cMax)
        {
            var range = ws.Range(r1, 1, r2, cMax);
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thick;
            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        }
    }
}
