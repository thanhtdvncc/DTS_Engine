using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DTS_Engine.Core.Data;
using DTS_Engine.Core.Engines;
using DTS_Engine.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace DTS_Engine.Commands
{
    /// <summary>
    /// Các l?nh ki?m toán t?i tr?ng SAP2000.
    /// H? tr? xu?t báo cáo th?ng kê chi ti?t theo t?ng, lo?i t?i và v? trí.
    ///
    /// TÍNH N?NG:
    /// - ??c toàn b? t?i tr?ng t? SAP2000 (Frame, Area, Point)
    /// - Nhóm theo t?ng và lo?i t?i
    /// - Tính t?ng di?n tích/chi?u dài
    /// - So sánh v?i ph?n l?c ?áy
    /// </summary>
    public class AuditCommands : CommandBase
    {
        #region Main Audit Command

        /// <summary>
        /// L?nh chính ?? ki?m toán t?i tr?ng SAP2000.
        /// Nh?p các Load Pattern c?n ki?m tra, xu?t báo cáo ra file text.
        /// </summary>
        [CommandMethod("DTS_AUDIT_SAP2000")]
        public void DTS_AUDIT_SAP2000()
        {
            ExecuteSafe(() =>
            {
                WriteMessage("\n==============================================");
                WriteMessage("      DTS ENGINE - AUDIT T?I TR?NG SAP2000     ");
                WriteMessage("==============================================");

                // 1. Ki?m tra k?t n?i SAP
                if (!EnsureSapConnection()) return;

                // 2. L?y danh sách Pattern có t?i (Smart Filter)
                WriteMessage("\n?ang quét d? li?u Load Patterns...");
                var activePatterns = SapUtils.GetActiveLoadPatterns();
                
                // L?c b? pattern r?ng (Total = 0) n?u danh sách quá dài
                var nonEmptyPatterns = activePatterns.Where(p => p.TotalEstimatedLoad > 0.001).ToList();
                if (nonEmptyPatterns.Count == 0) nonEmptyPatterns = activePatterns; // Fallback

                // 3. Xây d?ng Menu ch?n
                var pko = new PromptKeywordOptions("\nCh?n Load Pattern c?n ki?m toán:");
                pko.AllowNone = true;

                // Thêm 10 pattern n?ng nh?t vào menu
                int maxMenu = Math.Min(10, nonEmptyPatterns.Count);
                for (int i = 0; i < maxMenu; i++)
                {
                    pko.Keywords.Add(nonEmptyPatterns[i].Name);
                }
                
                if (nonEmptyPatterns.Count > maxMenu) pko.Keywords.Add("Other");
                pko.Keywords.Add("All");
                pko.Keywords.Default = nonEmptyPatterns[0].Name;

                // Hi?n th? danh sách g?i ý
                WriteMessage("\nDanh sách Pattern có t?i tr?ng l?n nh?t:");
                for (int i = 0; i < maxMenu; i++)
                {
                    WriteMessage($"  - {nonEmptyPatterns[i].Name} (Est: {nonEmptyPatterns[i].TotalEstimatedLoad:N0})");
                }

                PromptResult res = Ed.GetKeywords(pko);
                if (res.Status != PromptStatus.OK) return;

                List<string> selectedPatterns = new List<string>();

                if (res.StringResult == "All")
                {
                    selectedPatterns = nonEmptyPatterns.Select(p => p.Name).ToList();
                }
                else if (res.StringResult == "Other")
                {
                    var strOpt = new PromptStringOptions("\nNh?p tên Load Pattern (cách nhau d?u ph?y): ");
                    var strRes = Ed.GetString(strOpt);
                    if (strRes.Status == PromptStatus.OK)
                    {
                        selectedPatterns = strRes.StringResult.Split(',')
                            .Select(s => s.Trim())
                            .Where(s => !string.IsNullOrEmpty(s))
                            .ToList();
                    }
                }
                else
                {
                    selectedPatterns.Add(res.StringResult);
                }

                if (selectedPatterns.Count == 0) return;

                // 4. Ch?n ??n v?
                var unitOpt = new PromptKeywordOptions("\nCh?n ??n v? xu?t báo cáo [Ton/kN/kgf/lb]: ");
                unitOpt.Keywords.Add("Ton");
                unitOpt.Keywords.Add("kN");
                unitOpt.Keywords.Add("kgf");
                unitOpt.Keywords.Add("lb");
                unitOpt.Keywords.Default = "Ton"; // K? s? VN thích T?n
                var unitRes = Ed.GetKeywords(unitOpt);
                if (unitRes.Status != PromptStatus.OK) return;
                string selectedUnit = unitRes.StringResult;

                // 5. Ch?y Audit
                WriteMessage($"\n?ang x? lý {selectedPatterns.Count} patterns...");
                var engine = new AuditEngine();
                
                string tempFolder = Path.GetTempPath();
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var reportFiles = new List<string>();

                foreach (var pat in selectedPatterns)
                {
                    WriteMessage($"?ang x? lý {pat}...");
                    var report = engine.RunSingleAudit(pat);
                    
                    if (report.Stories.Count == 0)
                    {
                        WriteWarning($"  -> {pat}: Không tìm th?y d? li?u ho?c t?i tr?ng = 0.");
                        continue;
                    }

                    string fileName = $"DTS_Audit_{report.ModelName}_{pat}_{timestamp}.txt";
                    string filePath = Path.Combine(tempFolder, fileName);
                    string content = engine.GenerateTextReport(report, selectedUnit);

                    File.WriteAllText(filePath, content, Encoding.UTF8);
                    reportFiles.Add(filePath);
                }

                // 6. K?t qu?
                if (reportFiles.Count > 0)
                {
                    WriteSuccess($"?ã t?o {reportFiles.Count} báo cáo.");
                    
                    // M? file ??u tiên ngay l?p t?c (UX: Instant Feedback)
                    try 
                    { 
                        Process.Start(reportFiles[0]); 
                        if(reportFiles.Count > 1) WriteMessage($"Các file khác n?m t?i: {tempFolder}");
                    } 
                    catch { }
                }
                else
                {
                    WriteWarning("Không t?o ???c báo cáo nào (Model tr?ng ho?c không có t?i).");
                }
            });
        }

        #endregion

        #region Quick Summary Command

        /// <summary>
        /// L?nh xem tóm t?t nhanh t?i tr?ng theo Load Pattern
        /// </summary>
        [CommandMethod("DTS_LOAD_SUMMARY")]
        public void DTS_LOAD_SUMMARY()
        {
            ExecuteSafe(() =>
            {
                WriteMessage("\n=== TÓM T?T T?I TR?NG SAP2000 ===");

                if (!EnsureSapConnection())
                    return;

                // L?y t?t c? patterns
                var patterns = SapUtils.GetLoadPatterns();
                if (patterns.Count == 0)
                {
                WriteError("Không tìm th?y Load Pattern nào.");
                    return;
                }

                WriteMessage($"\nModel: {SapUtils.GetModelName()}");
                WriteMessage($"??n v?: {UnitManager.Info}");
                WriteMessage($"\nLoad Patterns ({patterns.Count}):");

                foreach (var pattern in patterns)
                {
                    // ??m s? t?i theo lo?i
                    int frameLoadCount = SapUtils.GetAllFrameDistributedLoads(pattern).Count;
                    int areaLoadCount = SapUtils.GetAllAreaUniformLoads(pattern).Count;
                    int pointLoadCount = SapUtils.GetAllPointLoads(pattern).Count;

                    int total = frameLoadCount + areaLoadCount + pointLoadCount;

                    if (total > 0)
                    {
                        WriteMessage($"  {pattern}: Frame={frameLoadCount}, Area={areaLoadCount}, Point={pointLoadCount}");
                    }
                    else
                    {
                        WriteMessage($"  {pattern}: (không có t?i)");
                    }
                }

                WriteMessage("\nDùng l?nh DTS_AUDIT_SAP2000 ?? xem chi ti?t.");
            });
        }

        #endregion

        #region List Elements Command

        /// <summary>
        /// Li?t kê ph?n t? có t?i theo pattern
        /// </summary>
        [CommandMethod("DTS_LIST_LOADED_ELEMENTS")]
        public void DTS_LIST_LOADED_ELEMENTS()
        {
            ExecuteSafe(() =>
            {
                WriteMessage("\n=== DANH SÁCH PH?N T? CÓ T?I ===");

                if (!EnsureSapConnection())
                    return;

                // Nh?p pattern
                var patternOpt = new PromptStringOptions("\nNh?p Load Pattern: ");
                patternOpt.DefaultValue = "DL";
                var patternRes = Ed.GetString(patternOpt);

                if (patternRes.Status != PromptStatus.OK)
                    return;

                string pattern = patternRes.StringResult.Trim().ToUpper();

                if (!SapUtils.LoadPatternExists(pattern))
                {
                    WriteError($"Load Pattern '{pattern}' không t?n t?i.");
                    return;
                }

                // L?y t?i
                var frameLoads = SapUtils.GetAllFrameDistributedLoads(pattern);
                var areaLoads = SapUtils.GetAllAreaUniformLoads(pattern);
                var pointLoads = SapUtils.GetAllPointLoads(pattern);

                WriteMessage($"\n--- {pattern} ---");

                // Frame loads
                if (frameLoads.Count > 0)
                {
                    WriteMessage($"\nFRAME DISTRIBUTED ({frameLoads.Count}):");
                    var grouped = frameLoads.GroupBy(l => l.Value1).OrderByDescending(g => g.Key);
                    foreach (var g in grouped.Take(10))
                    {
                        var names = g.Select(l => l.ElementName).Take(10);
                        WriteMessage($"  {g.Key:0.00} kN/m: {string.Join(", ", names)}{(g.Count() > 10 ? "..." : "")}");
                    }
                }

                // Area loads
                if (areaLoads.Count > 0)
                {
                    WriteMessage($"\nAREA UNIFORM ({areaLoads.Count}):");
                    var grouped = areaLoads.GroupBy(l => l.Value1).OrderByDescending(g => g.Key);
                    foreach (var g in grouped.Take(10))
                    {
                        var names = g.Select(l => l.ElementName).Take(10);
                        WriteMessage($"  {g.Key:0.00} kN/m²: {string.Join(", ", names)}{(g.Count() > 10 ? "..." : "")}");
                    }
                }

                // Point loads
                if (pointLoads.Count > 0)
                {
                    WriteMessage($"\nPOINT FORCE ({pointLoads.Count}):");
                    var grouped = pointLoads.GroupBy(l => l.Value1).OrderByDescending(g => g.Key);
                    foreach (var g in grouped.Take(10))
                    {
                        var names = g.Select(l => l.ElementName).Take(10);
                        WriteMessage($"  {g.Key:0.00} kN: {string.Join(", ", names)}{(g.Count() > 10 ? "..." : "")}");
                    }
                }

                int total = frameLoads.Count + areaLoads.Count + pointLoads.Count;
                WriteMessage($"\nT?ng: {total} b?n ghi t?i.");
            });
        }

        #endregion

        #region Reaction Check Command

        /// <summary>
        /// Ki?m tra ph?n l?c ?áy cho các load case
        /// </summary>
        [CommandMethod("DTS_CHECK_REACTIONS")]
        public void DTS_CHECK_REACTIONS()
        {
            ExecuteSafe(() =>
            {
                WriteMessage("\n=== KI?M TRA PH?N L?C ?ÁY ===");

                if (!EnsureSapConnection())
                    return;

                var patterns = SapUtils.GetLoadPatterns();

                WriteMessage($"\nModel: {SapUtils.GetModelName()}");
                WriteMessage("\nBase Reaction Z theo Load Pattern:");
                WriteMessage(new string('-', 50));

                bool hasAnyReaction = false;

                foreach (var pattern in patterns)
                {
                    double reaction = SapUtils.GetBaseReactionZ(pattern);
                    if (Math.Abs(reaction) > 0.01)
                    {
                        hasAnyReaction = true;
                        WriteMessage($"  {pattern,-15}: {reaction,15:n2} kN");
                    }
                }

                if (!hasAnyReaction)
                {
                    WriteWarning("\nKhông có ph?n l?c nào. Model có th? ch?a ???c phân tích.");
                    WriteMessage("Ch?y phân tích trong SAP2000 r?i th? l?i.");
                }
                else
                {
                    WriteMessage(new string('-', 50));
                }
            });
        }

        #endregion

        #region Export to CSV Command

        /// <summary>
        /// Xu?t d? li?u t?i sang CSV ?? x? lý trong Excel
        /// </summary>
        [CommandMethod("DTS_EXPORT_LOADS_CSV")]
        public void DTS_EXPORT_LOADS_CSV()
        {
            ExecuteSafe(() =>
            {
                WriteMessage("\n=== XU?T T?I TR?NG RA CSV ===");

                if (!EnsureSapConnection())
                    return;

                // Nh?p pattern
                var patternOpt = new PromptStringOptions("\nNh?p Load Pattern (ho?c * cho t?t c?): ");
                patternOpt.DefaultValue = "*";
                var patternRes = Ed.GetString(patternOpt);

                if (patternRes.Status != PromptStatus.OK)
                    return;

                string patternInput = patternRes.StringResult.Trim();
                bool exportAll = patternInput == "*";

                // Thu th?p d? li?u
                var allLoads = new List<RawSapLoad>();

                if (exportAll)
                {
                    allLoads.AddRange(SapUtils.GetAllFrameDistributedLoads());
                    allLoads.AddRange(SapUtils.GetAllAreaUniformLoads());
                    allLoads.AddRange(SapUtils.GetAllPointLoads());
                }
                else
                {
                    allLoads.AddRange(SapUtils.GetAllFrameDistributedLoads(patternInput));
                    allLoads.AddRange(SapUtils.GetAllAreaUniformLoads(patternInput));
                    allLoads.AddRange(SapUtils.GetAllPointLoads(patternInput));
                }

                if (allLoads.Count == 0)
                {
                    WriteWarning("Không có d? li?u t?i ?? xu?t.");
                    return;
                }

                // T?o CSV
                var sb = new StringBuilder();
                sb.AppendLine("Element,LoadPattern,LoadType,Value,Direction,Z");

                foreach (var load in allLoads)
                {
                    sb.AppendLine($"{load.ElementName},{load.LoadPattern},{load.LoadType},{load.Value1:0.00},{load.Direction},{load.ElementZ:0}");
                }

                string fileName = $"DTS_Loads_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                string filePath = Path.Combine(Path.GetTempPath(), fileName);

                File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);

                WriteSuccess($"?ã xu?t {allLoads.Count} b?n ghi ra:");
                WriteMessage($"  {filePath}");

                // M? file
                try
                {
                    Process.Start(filePath);
                }
                catch { }
            });
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// ??m b?o ?ã k?t n?i SAP2000
        /// </summary>
        private bool EnsureSapConnection()
        {
            if (SapUtils.IsConnected)
                return true;

            WriteMessage("?ang k?t n?i SAP2000...");

            if (!SapUtils.Connect(out string msg))
            {
                WriteError(msg);
                return false;
            }

            WriteSuccess(msg);
            return true;
        }

        #endregion
    }
}
