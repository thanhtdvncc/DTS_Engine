using DTS_Engine.Core.Data;
using DTS_Engine.Core.Utils;
using SAP2000v1;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DTS_Engine.Core.Engines
{
    /// <summary>
    /// Engine chuyên trách giao tiếp với module Design của SAP2000.
    /// Handles extraction of results and updating of rebar sections.
    /// </summary>
    public class SapDesignEngine
    {
        private readonly cSapModel _model;

        public SapDesignEngine()
        {
            if (SapUtils.IsConnected)
            {
                _model = SapUtils.GetModel();
            }
        }

        public bool IsReady => _model != null;

        /// <summary>
        /// Lấy kết quả thiết kế dầm (Summary Results) chuẩn theo tài liệu SAP2000.
        /// </summary>
        public Dictionary<string, BeamResultData> GetBeamResults(List<string> frameNames)
        {
            var results = new Dictionary<string, BeamResultData>();
            if (_model == null || frameNames == null || frameNames.Count == 0) return results;

            // Thiết lập đơn vị kN_cm_C
            // -> Area sẽ là cm2
            // -> Area/Length sẽ là cm2/cm
            var originalUnit = _model.GetPresentUnits();
            try
            {
                _model.SetPresentUnits(eUnits.kN_cm_C);

                foreach (var name in frameNames)
                {
                    // Các biến hứng dữ liệu từ API (Ref Parameters)
                    int numberItems = 0;
                    string[] frames = null;
                    double[] location = null;

                    // Flexure (Uốn)
                    string[] topCombo = null; double[] topArea = null; // TopArea [L2]
                    string[] botCombo = null; double[] botArea = null; // BotArea [L2]

                    // Shear (Cắt)
                    string[] vMajorCombo = null; double[] vMajorArea = null; // VmajorArea [L2/L] -> Av/s

                    // Torsion (Xoắn)
                    string[] tlCombo = null; double[] tlArea = null; // TLArea [L2] -> Total Longitudinal Al
                    string[] ttCombo = null; double[] ttArea = null; // TTArea [L2/L] -> Transverse At/s

                    // Errors
                    string[] errorSummary = null; string[] warningSummary = null;

                    // Gọi API chuẩn theo thứ tự tham số tài liệu cung cấp
                    int ret = _model.DesignConcrete.GetSummaryResultsBeam(
                        name,
                        ref numberItems,
                        ref frames,
                        ref location,
                        ref topCombo, ref topArea,
                        ref botCombo, ref botArea,
                        ref vMajorCombo, ref vMajorArea, // Shear
                        ref tlCombo, ref tlArea,         // Torsion Long
                        ref ttCombo, ref ttArea,         // Torsion Trans
                        ref errorSummary, ref warningSummary,
                        eItemType.Objects);

                    if (ret == 0 && numberItems > 0)
                    {
                        var data = new BeamResultData();
                        var dtsSettings = DtsSettings.Instance;

                        // --- BLOCK QUAN TRỌNG: Lấy Width/Height chính xác ---
                        string propName = "";
                        string sAuto = "";
                        if (_model.FrameObj.GetSection(name, ref propName, ref sAuto) == 0)
                        {
                            data.SectionName = propName;
                            string matProp = "";
                            double t3 = 0, t2 = 0; // t3=Depth, t2=Width
                            int color = -1;
                            string notes = "", guid = "";

                            // Cố gắng lấy properties hình chữ nhật
                            if (_model.PropFrame.GetRectangle(propName, ref propName, ref matProp, ref t3, ref t2, ref color, ref notes, ref guid) == 0)
                            {
                                data.SectionHeight = t3;
                                data.Width = t2;
                                data.ConcreteGrade = matProp;
                                data.SteelGrade = dtsSettings.General?.SteelGradeName ?? ""; // No fallback
                            }
                            else
                            {
                                // No fallback, keep as 0 if not rectangle
                            }
                        }
                        // ----------------------------------------------------

                        // Helper lấy max trong vùng
                        // [FIX] Dùng DtsSettings thay vì RebarSettings
                        double L = location[numberItems - 1];
                        double zoneStartRatio = dtsSettings.General?.ZoneL1_Ratio ?? 0.25;
                        double zoneEndRatio = dtsSettings.General?.ZoneL2_Ratio ?? 0.25;
                        double limitStart = L * zoneStartRatio;
                        double limitEnd = L * (1.0 - zoneEndRatio);

                        // Gán dữ liệu vào 3 vùng
                        for (int z = 0; z < 3; z++)
                        {
                            double start = z == 0 ? 0 : (z == 1 ? limitStart : limitEnd);
                            double end = z == 0 ? limitStart : (z == 1 ? limitEnd : L);

                            // Helpers to find max and recording index/combo
                            int idxTop = -1; double maxTop = -1;
                            int idxBot = -1; double maxBot = -1;
                            int idxShear = -1; double maxShear = -1;
                            int idxTor = -1; double maxTor = -1;

                            for (int i = 0; i < numberItems; i++)
                            {
                                double loc = location[i];
                                if (loc >= start - 0.001 && loc <= end + 0.001)
                                {
                                    if (topArea[i] > maxTop) { maxTop = topArea[i]; idxTop = i; }
                                    if (botArea[i] > maxBot) { maxBot = botArea[i]; idxBot = i; }
                                    if (vMajorArea[i] > maxShear) { maxShear = vMajorArea[i]; idxShear = i; }
                                    if (tlArea[i] > maxTor) { maxTor = tlArea[i]; idxTor = i; }
                                }
                            }

                            data.TopArea[z] = maxTop;
                            data.BotArea[z] = maxBot;
                            data.ShearArea[z] = maxShear;
                            data.TorsionArea[z] = maxTor;
                            data.TTArea[z] = idxTor >= 0 ? ttArea[idxTor] : 0;

                            if (idxTop >= 0) data.TopCombo[z] = topCombo[idxTop];
                            if (idxBot >= 0) data.BotCombo[z] = botCombo[idxBot];
                            if (idxShear >= 0) data.ShearCombo[z] = vMajorCombo[idxShear];
                            if (idxTor >= 0) data.TorsionCombo[z] = tlCombo[idxTor];

                            // Detailed Traceability: Unique locations for each rebar type
                            if (idxTop >= 0)
                            {
                                data.TopSapNo[z] = frames[idxTop];
                                data.TopLocMm[z] = Math.Round(location[idxTop] * 10.0);
                            }
                            if (idxBot >= 0)
                            {
                                data.BotSapNo[z] = frames[idxBot];
                                data.BotLocMm[z] = Math.Round(location[idxBot] * 10.0);
                            }
                            if (idxShear >= 0)
                            {
                                data.ShearSapNo[z] = frames[idxShear];
                                data.ShearLocMm[z] = Math.Round(location[idxShear] * 10.0);
                            }
                            if (idxTor >= 0)
                            {
                                data.TorsionSapNo[z] = frames[idxTor];
                                data.TorsionLocMm[z] = Math.Round(location[idxTor] * 10.0);
                            }

                            // Legacy primary reference (keep for compatibility)
                            int refIdx = idxTop >= 0 ? idxTop : (idxShear >= 0 ? idxShear : (idxBot >= 0 ? idxBot : -1));
                            if (refIdx >= 0)
                            {
                                data.SapElementNos[z] = frames[refIdx];
                                data.LocationMm[z] = Math.Round(location[refIdx] * 10.0); // kN_cm_C context, location is in cm. mm = cm * 10.
                            }

                            // Fetch Forces for each critical point in this zone
                            data.TopMoment[z] = idxTop >= 0 ? GetForceValue(name, data.TopCombo[z], location[idxTop], "M3") : 0;
                            data.BotMoment[z] = idxBot >= 0 ? GetForceValue(name, data.BotCombo[z], location[idxBot], "M3") : 0;
                            data.ShearForce[z] = idxShear >= 0 ? GetForceValue(name, data.ShearCombo[z], location[idxShear], "V2") : 0;
                            data.TorsionMoment[z] = idxTor >= 0 ? GetForceValue(name, data.TorsionCombo[z], location[idxTor], "T") : 0;
                        }

                        data.DesignCombo = topCombo[0];
                        data.SapElementName = name;
                        results[name] = data;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GetBeamResults Error: " + ex.Message);
            }
            finally
            {
                _model.SetPresentUnits(originalUnit);
            }

            return results;
        }

        /// <summary>
        /// Gán thép thực tế vào Dầm (Update SAP).
        /// Quy trình:
        /// 1. Kiểm tra/Tạo Section mới (dựa trên tên tiết diện cũ + hậu tố rebar).
        /// 2. Gán thép cho Section mới (SetRebarBeam).
        /// 3. Gán Section mới cho Frame.
        /// </summary>
        public bool UpdateBeamRebar(string frameName, string newSectionName,
            double[] topAreaProv, double[] botAreaProv,
            double coverTop, double coverBot)
        {
            if (_model == null) return false;

            // [FIX] Set units to kN_cm_C to ensure rebar areas are in cm²
            var originalUnit = _model.GetPresentUnits();
            try
            {
                _model.SetPresentUnits(eUnits.kN_cm_C);

                // 1. Lấy tiết diện hiện tại của Frame để clone (nếu chưa có section mới)
                string propName = "";
                string sAuto = "";
                if (_model.FrameObj.GetSection(frameName, ref propName, ref sAuto) != 0) return false;

                // Nếu propName đã đúng là newSectionName thì ok, nếu khác thì cần tạo mới/kiểm tra
                if (propName != newSectionName)
                {
                    // Kiểm tra newSectionName có chưa
                    // Cách đơn giản: Thử GetSection, nếu fail tức là chưa có -> Clone
                    // Tuy nhiên SAP ko có lệnh "Exist". Ta dùng PropFrame.GetNameList
                    if (!SectionExists(newSectionName))
                    {
                        // Clone từ propName gốc
                        // SAP API không có Clone trực tiếp nhanh.
                        // Workaround: Get Prop Data -> Set Prop Data New Name.
                        // Tạm thời giả định module này sẽ được gọi sau khi đã có prop data.
                        // Nhưng để robust, ta cần implement CloneSection.
                        if (!CloneConcreteSection(propName, newSectionName)) return false;
                    }

                    // Gán Section mới cho Frame
                    _model.FrameObj.SetSection(frameName, newSectionName, eItemType.Objects);
                }

                // 2. Set Rebar cho Section (newSectionName)
                // SetRebarBeam requires Material Names, Covers, and Areas.
                // We assume MatPropLong is same as used in original, or we fetch it.
                // For simplicity, let's try to get existing rebar props first.

                string matLong = "", matConf = "";
                double cTop = 0, cBot = 0, tl = 0, tr = 0, bl = 0, br = 0;

                // Get existing rebar to get Materials
                if (_model.PropFrame.GetRebarBeam(newSectionName, ref matLong, ref matConf, ref cTop, ref cBot, ref tl, ref tr, ref bl, ref br) != 0)
                {
                    // Nếu chưa có rebar data, có thể do section mới clone chưa set.
                    // Lấy từ section gốc 'propName' (trước khi đổi tên)
                    _model.PropFrame.GetRebarBeam(propName, ref matLong, ref matConf, ref cTop, ref cBot, ref tl, ref tr, ref bl, ref br);
                }

                // Update Values
                // SAP SetRebarBeam inputs: TopLeft, TopRight, BotLeft, BotRight.
                // Mapping:
                // TopLeft -> TopAreaProv[0] (Start)
                // TopRight -> TopAreaProv[2] (End)
                // BotLeft -> BotAreaProv[0] (Start)
                // BotRight -> BotAreaProv[2] (End)

                double topStart = topAreaProv[0];
                double topEnd = topAreaProv[2];
                double botStart = botAreaProv[0];
                double botEnd = botAreaProv[2];

                // Cover: mm -> cm (SAP units now kN_cm_C)
                int retRebar = _model.PropFrame.SetRebarBeam(newSectionName, matLong, matConf,
                    coverTop / 10.0, coverBot / 10.0,
                    topStart, topEnd, botStart, botEnd);

                return retRebar == 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("UpdateBeamRebar Error: " + ex.Message);
                return false;
            }
            finally
            {
                // Restore original units
                _model.SetPresentUnits(originalUnit);
            }
        }

        private bool SectionExists(string name)
        {
            int count = 0;
            string[] names = null;
            _model.PropFrame.GetNameList(ref count, ref names);
            return names != null && names.Contains(name);
        }

        private bool CloneConcreteSection(string sourceName, string destName)
        {
            // Simple Clone for Rectangular Section
            string matProp = "";
            string fileName = ""; // Dummy for first ref param
            double t3 = 0, t2 = 0;
            int color = -1;
            string notes = "", guid = "";

            // Try GetRectangular
            if (_model.PropFrame.GetRectangle(sourceName, ref fileName, ref matProp, ref t3, ref t2, ref color, ref notes, ref guid) == 0)
            {
                return _model.PropFrame.SetRectangle(destName, matProp, t3, t2, -1, notes, "") == 0;
            }

            // WARNING: Non-rectangular sections not supported yet
            System.Diagnostics.Debug.WriteLine($"[SapDesignEngine] WARNING: Cannot clone section '{sourceName}' - not a rectangular section (T, I, etc. not supported).");
            return false;
        }

        #region Smart Section Management

        /// <summary>
        /// Lấy danh sách tất cả Frame Sections trong model.
        /// </summary>
        public List<string> GetAllBeamSections()
        {
            var sections = new List<string>();
            if (_model == null) return sections;

            int count = 0;
            string[] names = null;
            if (_model.PropFrame.GetNameList(ref count, ref names) == 0 && names != null)
            {
                sections.AddRange(names);
            }
            return sections;
        }

        /// <summary>
        /// Lấy danh sách sections đang được sử dụng bởi các frames.
        /// </summary>
        public HashSet<string> GetUsedSections()
        {
            var usedSections = new HashSet<string>();
            if (_model == null) return usedSections;

            // Lấy tất cả frames
            int count = 0;
            string[] frameNames = null;
            _model.FrameObj.GetNameList(ref count, ref frameNames);

            if (frameNames == null) return usedSections;

            foreach (var frame in frameNames)
            {
                string propName = "";
                string sAuto = "";
                if (_model.FrameObj.GetSection(frame, ref propName, ref sAuto) == 0)
                {
                    usedSections.Add(propName);
                }
            }
            return usedSections;
        }

        /// <summary>
        /// Đảm bảo section tồn tại với đúng dimensions. Nếu chưa có → tạo mới.
        /// Trả về true nếu section sẵn sàng sử dụng.
        /// </summary>
        /// <param name="sectionName">Tên section (VD: "B101", "G205")</param>
        /// <param name="width">Bề rộng (mm)</param>
        /// <param name="height">Chiều cao (mm)</param>
        /// <param name="material">Vật liệu (VD: "C25", "C30")</param>
        public SectionSyncResult EnsureSection(string sectionName, double width, double height, string material = "C25")
        {
            if (_model == null)
                return new SectionSyncResult { Success = false, Message = "SAP Model not connected" };

            try
            {
                // Convert mm -> m (SAP default units)
                double widthM = width / 1000.0;
                double heightM = height / 1000.0;

                if (SectionExists(sectionName))
                {
                    // Kiểm tra dimensions có khớp không
                    string matProp = "";
                    string fileName = "";
                    double t3 = 0, t2 = 0;
                    int color = -1;
                    string notes = "", guid = "";

                    if (_model.PropFrame.GetRectangle(sectionName, ref fileName, ref matProp, ref t3, ref t2, ref color, ref notes, ref guid) == 0)
                    {
                        // So sánh dimensions (tolerance 1mm = 0.001m)
                        bool sameSize = Math.Abs(t3 - heightM) < 0.001 && Math.Abs(t2 - widthM) < 0.001;

                        if (sameSize)
                        {
                            return new SectionSyncResult
                            {
                                Success = true,
                                Action = SectionAction.NoChange,
                                Message = $"Section '{sectionName}' already exists with correct size"
                            };
                        }
                        else
                        {
                            // Size khác → Update
                            if (_model.PropFrame.SetRectangle(sectionName, material, heightM, widthM, -1, "", "") == 0)
                            {
                                return new SectionSyncResult
                                {
                                    Success = true,
                                    Action = SectionAction.Updated,
                                    Message = $"Section '{sectionName}' updated: {t2 * 1000:F0}x{t3 * 1000:F0} → {width:F0}x{height:F0}"
                                };
                            }
                            else
                            {
                                return new SectionSyncResult
                                {
                                    Success = false,
                                    Message = $"Failed to update section '{sectionName}'"
                                };
                            }
                        }
                    }
                }

                // Section chưa tồn tại → Tạo mới
                if (_model.PropFrame.SetRectangle(sectionName, material, heightM, widthM, -1, "", "") == 0)
                {
                    return new SectionSyncResult
                    {
                        Success = true,
                        Action = SectionAction.Created,
                        Message = $"Section '{sectionName}' created: {width:F0}x{height:F0}"
                    };
                }
                else
                {
                    return new SectionSyncResult
                    {
                        Success = false,
                        Message = $"Failed to create section '{sectionName}'"
                    };
                }
            }
            catch (Exception ex)
            {
                return new SectionSyncResult
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Xóa các sections không còn được sử dụng bởi bất kỳ frame nào.
        /// Chỉ xóa sections có pattern "_DTS" hoặc theo prefix cụ thể.
        /// </summary>
        /// <param name="prefixFilter">Chỉ xóa sections có prefix này (VD: "B", "G"). Null = xóa tất cả unused.</param>
        /// <returns>Số lượng sections đã xóa</returns>
        public int CleanupUnusedSections(string prefixFilter = null)
        {
            if (_model == null) return 0;

            var allSections = GetAllBeamSections();
            var usedSections = GetUsedSections();
            int deletedCount = 0;

            foreach (var section in allSections)
            {
                // Bỏ qua sections đang sử dụng
                if (usedSections.Contains(section)) continue;

                // Nếu có filter, chỉ xóa sections match filter
                if (!string.IsNullOrEmpty(prefixFilter) && !section.StartsWith(prefixFilter))
                    continue;

                // Thử xóa section
                try
                {
                    if (_model.PropFrame.Delete(section) == 0)
                    {
                        deletedCount++;
                        System.Diagnostics.Debug.WriteLine($"[SapDesignEngine] Deleted unused section: {section}");
                    }
                }
                catch
                {
                    // SAP có thể không cho xóa một số sections đặc biệt
                }
            }

            return deletedCount;
        }

        /// <summary>
        /// Gán section cho danh sách frames.
        /// </summary>
        public int AssignSectionToFrames(string sectionName, List<string> frameNames)
        {
            if (_model == null || frameNames == null) return 0;

            int successCount = 0;
            foreach (var frame in frameNames)
            {
                if (_model.FrameObj.SetSection(frame, sectionName, eItemType.Objects) == 0)
                    successCount++;
            }
            return successCount;
        }

        private double GetForceValue(string frameName, string comboName, double targetLoc, string forceType)
        {
            if (string.IsNullOrEmpty(comboName) || _model == null) return 0;

            int numberItems = 0;
            string[] obj = null;
            double[] objSta = null;
            string[] elm = null;
            double[] elmSta = null;
            string[] loadCase = null;
            string[] stepType = null;
            double[] stepNum = null;
            double[] p = null, v2 = null, v3 = null, t = null, m2 = null, m3 = null;

            // Lấy nội lực cho combo cụ thể
            _model.Results.Setup.DeselectAllCasesAndCombosForOutput();
            _model.Results.Setup.SetComboSelectedForOutput(comboName, true);

            int ret = _model.Results.FrameForce(frameName, (eItemTypeElm)eItemType.Objects, ref numberItems,
                ref obj, ref objSta, ref elm, ref elmSta, ref loadCase, ref stepType, ref stepNum,
                ref p, ref v2, ref v3, ref t, ref m2, ref m3);

            if (ret != 0 || numberItems == 0) return 0;

            // Tìm vị trí khớp hoặc gần nhất với targetLoc
            double minDiff = double.MaxValue;
            double foundVal = 0;

            for (int i = 0; i < numberItems; i++)
            {
                double diff = Math.Abs(objSta[i] - targetLoc);
                if (diff < minDiff)
                {
                    minDiff = diff;
                    switch (forceType)
                    {
                        case "M3": foundVal = m3[i]; break;
                        case "V2": foundVal = v2[i]; break;
                        case "T": foundVal = t[i]; break;
                        case "P": foundVal = p[i]; break;
                        default: foundVal = 0; break;
                    }
                }
                if (diff < 0.001) break; // Khớp hoàn hảo
            }

            return foundVal;
        }

        #endregion
    }

    #region Section Sync Result

    public enum SectionAction
    {
        NoChange,
        Created,
        Updated,
        Deleted
    }

    public class SectionSyncResult
    {
        public bool Success { get; set; }
        public SectionAction Action { get; set; }
        public string Message { get; set; }
    }

    #endregion
}
