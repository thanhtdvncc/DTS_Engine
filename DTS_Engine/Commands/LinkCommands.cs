using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DTS_Engine.Core.Data;
using DTS_Engine.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DTS_Engine.Commands
{
    /// <summary>
    /// Quản lý liên kết (Link) với cơ chế Audit thông minh & An toàn (Safe Mode)
    /// </summary>
    public class LinkCommands : CommandBase
    {
        #region 1. DTS_LINK_ORIGIN (Gán phần tử vào Story/Trục)

        /// <summary>
        /// Lệnh cũ DTS_LINK -> Đổi tên thành DTS_LINK_ORIGIN
        /// Chuyên dùng để gán các phần tử vào Story Origin
        /// </summary>
        [CommandMethod("DTS_LINK_ORIGIN")]
        public void DTS_LINK_ORIGIN()
        {
            WriteMessage("\n=== LIÊN KẾT VỚI ORIGIN (STORY) ===");
            
            // 1. Quét chọn vùng
            WriteMessage("Quét chọn vùng chứa Origin và các phần tử...");
            var allIds = AcadUtils.SelectObjectsOnScreen("LINE,LWPOLYLINE,POLYLINE,CIRCLE");
            if (allIds.Count == 0) return;

            ObjectId originId = ObjectId.Null;
            List<ObjectId> childIds = new List<ObjectId>();

            UsingTransaction(tr =>
            {
                foreach (ObjectId id in allIds)
                {
                    if (id.IsErased) continue;
                    
                    // Kiểm tra an toàn
                    DBObject obj = SafeGetObject(tr, id, OpenMode.ForRead);
                    if (obj == null) continue;

                    // Phân loại
                    if (XDataUtils.ReadStoryData(obj) != null)
                        originId = id;
                    else if (XDataUtils.ReadElementData(obj) != null)
                        childIds.Add(id);
                }
            });

            if (originId == ObjectId.Null)
            {
                WriteError("Không tìm thấy Origin nào trong vùng chọn!");
                return;
            }

            if (childIds.Count == 0)
            {
                WriteMessage("Không có phần tử con nào để liên kết.");
                return;
            }

            // Thực hiện Link
            ExecuteLink(childIds, originId, isStoryOrigin: true);
        }

        #endregion

        #region 2. DTS_LINK (Liên kết Cha - Con kết cấu)

        /// <summary>
        /// Lệnh mới: Link Cha - Con theo cấu trúc cây (Dầm -> Cột, Sàn -> Dầm)
        /// Quy trình: Chọn nhiều Con -> Chọn 1 Cha
        /// </summary>
        [CommandMethod("DTS_LINK")]
        public void DTS_LINK()
        {
            // UI: professional wording required by product spec
            WriteMessage("\n=== Tạo liên kết phần tử ===");

            // Bước 1: Chọn Con
            WriteMessage("\n1. Chọn các phần tử CON cần liên kết:");
            var childIds = AcadUtils.SelectObjectsOnScreen("LINE,LWPOLYLINE,POLYLINE,CIRCLE");
            if (childIds.Count == 0) return;

            // Bước 2: Chọn Cha
            PromptEntityOptions peo = new PromptEntityOptions("\n2. Chọn 1 phần tử CHA:");
            // Allow any object to be selected as parent (user requested this behavior)
            var per = Ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            ObjectId parentId = per.ObjectId;

            // Validate: Cha không nằm trong đám con
            if (childIds.Contains(parentId))
            {
                childIds.Remove(parentId);
                WriteMessage(" (Đã loại bỏ Cha khỏi danh sách Con)");
            }

            // Thực hiện Link
            ExecuteLink(childIds, parentId, isStoryOrigin: false);
        }

        #endregion

        #region Core Linking Logic

        private void ExecuteLink(List<ObjectId> childIds, ObjectId parentId, bool isStoryOrigin)
        {
            int success = 0, failed = 0, loops = 0;
            // Keep lists for visualization and reporting
            var newlyLinked = new List<ObjectId>();
            // reason -> detail -> list of ids
            var skippedByReason = new Dictionary<string, Dictionary<string, List<ObjectId>>>();
            // Protect UI wording: do not change messages below without review
            // UI_PROTECTION_START
            string parentInfo = parentId.Handle.ToString();

            UsingTransaction(tr =>
            {
                DBObject parentObj = SafeGetObject(tr, parentId, OpenMode.ForWrite);
                if (parentObj == null) return;

                // Xác định Parent Type & Data
                string parentHandle = parentObj.Handle.ToString();
                ElementType parentType = ElementType.Unknown;
                
                var storyData = XDataUtils.ReadStoryData(parentObj);
                var parentElemData = XDataUtils.ReadElementData(parentObj);

                if (storyData != null) { parentType = ElementType.StoryOrigin; parentInfo = $"Origin {storyData.StoryName} {storyData.Elevation:0}mm"; }
                else if (parentElemData != null) parentType = parentElemData.ElementType;
                else
                {
                    WriteError("Đối tượng CHA chưa được gán Type (DTS Data).");
                    return;
                }

                foreach (ObjectId childId in childIds)
                {
                    Entity childEnt = SafeGetObject(tr, childId, OpenMode.ForWrite) as Entity;
                    if (childEnt == null) continue;

                    var childData = XDataUtils.ReadElementData(childEnt);
                    if (childData == null)
                    {
                        AddSkip("NoData", childId, "No DTS data");
                        failed++; continue; // Hoặc tự động gán default? Tùy policy.
                    }

                    // Rule 1: Hierarchy Check
                    if (!LinkRules.CanBePrimaryParent(parentType, childData.ElementType))
                    {
                        string detail = $"{childData.ElementType}->{parentType}";
                        AddSkip("Hierarchy", childId, detail);
                        WriteError($"Bỏ qua {childEnt.Handle}: Sai phân cấp ({childData.ElementType} -> {parentType})");
                        failed++;
                        continue;
                    }

                    // Rule 2: Acyclic Check (Chỉ áp dụng nếu cha không phải là Story)
                    if (!isStoryOrigin)
                    {
                        if (LinkRules.DetectCycle(parentObj, childEnt.Handle.ToString(), tr))
                        {
                        AddSkip("Cycle", childId, "cycle detected");
                        WriteError($"Bỏ qua {childEnt.Handle}: Tham chiếu vòng (child là ancestor của parent)");
                        loops++;
                        continue;
                        }
                    }

                    // Xử lý Link cũ (nếu có) -> Unlink sạch sẽ
                    if (childData.IsLinked && childData.OriginHandle != parentHandle)
                    {
                        XDataUtils.RemoveLinkTwoWay(childEnt, tr);
                        childData = XDataUtils.ReadElementData(childEnt); // Reload
                    }

                    // CẬP NHẬT CON
                    childData.OriginHandle = parentHandle;
                    // Kế thừa cao độ nếu cha là Story
                    if (storyData != null)
                    {
                        childData.BaseZ = storyData.Elevation;
                        childData.Height = storyData.StoryHeight;
                    }
                    XDataUtils.WriteElementData(childEnt, childData, tr);

                    // CẬP NHẬT CHA (Thêm handle con vào danh sách)
                    if (storyData != null)
                    {
                        if (!storyData.ChildHandles.Contains(childEnt.Handle.ToString()))
                            storyData.ChildHandles.Add(childEnt.Handle.ToString());
                    }
                    else if (parentElemData != null)
                    {
                        if (!parentElemData.ChildHandles.Contains(childEnt.Handle.ToString()))
                            parentElemData.ChildHandles.Add(childEnt.Handle.ToString());
                    }

                    success++;
                    newlyLinked.Add(childId);
                }

                // Lưu lại CHA
                if (storyData != null) XDataUtils.WriteStoryData(parentObj, storyData, tr);
                else if (parentElemData != null)
                {
                    XDataUtils.WriteElementData(parentObj, parentElemData, tr);
                    // update parentInfo with type and handle
                    parentInfo = $"{parentElemData.ElementType} (handle {parentObj.Handle})";
                }
            });

            // Draw highlights/visual links for newly linked and skipped items
            if (newlyLinked.Count > 0)
            {
                // draw links for the group
                DrawLinkLines(parentId, newlyLinked);
                HighlightObjects(newlyLinked, 3); // highlight linked items (color index 3)
            }

            // Skipped items visual: draw links in different colors per reason and highlight
            foreach (var reasonKvp in skippedByReason)
            {
                string reason = reasonKvp.Key;
                // choose colorIndex per reason
                int colorIndexForReason = reason == "Hierarchy" ? 1 : (reason == "Cycle" ? 6 : 4);
                // flatten lists
                var flatList = new List<ObjectId>();
                foreach (var det in reasonKvp.Value)
                {
                    flatList.AddRange(det.Value);
                }
                if (flatList.Count == 0) continue;
                DrawLinkLines(parentId, flatList, colorIndexForReason);
                HighlightObjects(flatList, colorIndexForReason);
            }

            // Reporting summary (professional wording)
            if (success > 0) WriteSuccess($"Đã link {success} phần tử vào {parentInfo}.");

            // Aggregate skipped messages by reason
            if (skippedByReason.Count > 0)
            {
                foreach (var kv in skippedByReason)
                {
                    string reasonKey = kv.Key;
                    int total = kv.Value.Values.Sum(l => l.Count);
                    if (reasonKey == "Hierarchy")
                    {
                        // group by detail for hierarchy
                        foreach (var det in kv.Value)
                        {
                            WriteMessage($"Bỏ qua {det.Value.Count} phần tử do phân cấp: {det.Key}.");
                        }
                    }
                    else if (reasonKey == "Cycle")
                    {
                        WriteMessage($"Bỏ qua {total} phần tử do tham chiếu vòng (mô tả: child là ancestor của parent).");
                    }
                    else if (reasonKey == "NoData")
                    {
                        WriteMessage($"Bỏ qua {total} phần tử do không có dữ liệu DTS (chưa gán type).");
                    }
                    else
                    {
                        WriteMessage($"Bỏ qua {total} phần tử ({reasonKey}).");
                    }
                }
            }

            if (loops > 0) WriteError($"Phát hiện {loops} trường hợp vòng lặp.");

            // UI_PROTECTION_END

            // Local helper to record skips
            void AddSkip(string reason, ObjectId id, string detail)
            {
                if (!skippedByReason.ContainsKey(reason)) skippedByReason[reason] = new Dictionary<string, List<ObjectId>>();
                var dict = skippedByReason[reason];
                if (string.IsNullOrEmpty(detail)) detail = "";
                if (!dict.ContainsKey(detail)) dict[detail] = new List<ObjectId>();
                dict[detail].Add(id);
            }
        }

        #endregion

        #region 3. SHOW LINK & SMART AUDIT (Xử lý xóa/lỗi)

        [CommandMethod("DTS_SHOW_LINK")]
        public void DTS_SHOW_LINK()
        {
            WriteMessage("\n=== HIỂN THỊ LIÊN KẾT & KIỂM TRA ===");
            var ids = AcadUtils.SelectObjectsOnScreen("LINE,LWPOLYLINE,POLYLINE,CIRCLE");
            if (ids.Count == 0) return;

            // Danh sách cần xử lý
            var orphans = new List<ObjectId>(); // Con mất cha
            var ghostRefs = new Dictionary<ObjectId, int>(); // Cha chứa con ma
            var validLinks = new Dictionary<ObjectId, List<ObjectId>>(); // Map Cha -> List<Con> để vẽ

            UsingTransaction(tr =>
            {
                foreach (ObjectId id in ids)
                {
                    if (id.IsErased) continue;
                    DBObject obj = SafeGetObject(tr, id, OpenMode.ForRead);
                    if (obj == null) continue;

                    // 1. KIỂM TRA VAI TRÒ LÀ CON (Check chiều Con -> Cha)
                    var elemData = XDataUtils.ReadElementData(obj);
                    if (elemData != null && elemData.IsLinked)
                    {
                        ObjectId parentId = AcadUtils.GetObjectIdFromHandle(elemData.OriginHandle);
                        
                        // Kiểm tra cha tồn tại không?
                        if (IsValidObject(tr, parentId))
                        {
                            if (!validLinks.ContainsKey(parentId)) validLinks[parentId] = new List<ObjectId>();
                            validLinks[parentId].Add(id);
                        }
                        else
                        {
                            // CHA ĐÃ BỊ XÓA -> ĐÂY LÀ TRẺ MỒ CÔI (ORPHAN)
                            orphans.Add(id);
                        }
                    }

                    // 2. KIỂM TRA VAI TRÒ LÀ CHA (Check chiều Cha -> Con)
                    List<string> childHandles = null;
                    var storyData = XDataUtils.ReadStoryData(obj);
                    if (storyData != null) childHandles = storyData.ChildHandles;
                    else if (elemData != null) childHandles = elemData.ChildHandles;

                    if (childHandles != null && childHandles.Count > 0)
                    {
                        int ghosts = 0;
                        foreach (string h in childHandles)
                        {
                            ObjectId cId = AcadUtils.GetObjectIdFromHandle(h);
                            if (!IsValidObject(tr, cId))
                            {
                                ghosts++; // Con đã bị xóa (Ghost Child)
                            }
                            else
                            {
                                // Nếu con còn sống, thêm vào map để vẽ (nếu chưa có)
                                if (!validLinks.ContainsKey(id)) validLinks[id] = new List<ObjectId>();
                                if (!validLinks[id].Contains(cId)) validLinks[id].Add(cId);
                            }
                        }
                        if (ghosts > 0) ghostRefs[id] = ghosts;
                    }
                }
            });

            // --- XỬ LÝ 1: VẼ LINK HỢP LỆ ---
            if (validLinks.Count > 0)
            {
                int drawn = 0;
                // UI protection: maintain concise professional messages and avoid per-element lists
                // UI_PROTECTION_START
                // Aggregate by parent type/name (e.g., 'Tường -> Origin 1 500mm')
                var summary = new Dictionary<string, int>();

                foreach (var kv in validLinks)
                {
                    var pId = kv.Key;
                    string pInfo = pId.Handle.ToString();
                    UsingTransaction(tr =>
                    {
                        var pObj = SafeGetObject(tr, pId, OpenMode.ForRead);
                        if (pObj != null)
                        {
                            var pStory = XDataUtils.ReadStoryData(pObj);
                            var pElem = XDataUtils.ReadElementData(pObj);
                            if (pStory != null) pInfo = $"Origin {pStory.StoryName} {pStory.Elevation:0}mm";
                            else if (pElem != null) pInfo = $"{pElem.ElementType} -> (handle {pObj.Handle})";
                        }
                    });

                    // Count types (use parent info as key)
                    if (!summary.ContainsKey(pInfo)) summary[pInfo] = 0;
                    summary[pInfo] += kv.Value.Count;

                    DrawLinkLines(pId, kv.Value);
                    drawn += kv.Value.Count;
                }

                // Print concise summaries
                foreach (var s in summary)
                {
                    // Example: "Đã vẽ 226 đường link từ Tường -> Origin 1 500mm"
                    WriteSuccess($"Đã vẽ {s.Value} đường link từ {InferChildType(validLinks)} -> {s.Key}.");
                }

                // UI_PROTECTION_END
            }

            // --- XỬ LÝ 2: DỌN DẸP "CON MA" (GARBAGE COLLECTION) ---
            // Đây là data rác (con đã bị xóa), nên dọn dẹp để nhẹ bản vẽ
            if (ghostRefs.Count > 0)
            {
                int totalGhosts = ghostRefs.Values.Sum();
                WriteMessage($"\n[INFO] Đang dọn dẹp {totalGhosts} tham chiếu rác (con đã bị xóa)...");
                
                UsingTransaction(tr =>
                {
                    foreach (var kv in ghostRefs)
                    {
                        CleanUpGhostChildren(tr, kv.Key);
                    }
                });
                WriteSuccess("Đã làm sạch dữ liệu Cha.");
            }

            // --- XỬ LÝ 3: GIẢI QUYẾT "MỒ CÔI" (ORPHANS) - QUAN TRỌNG ---
            if (orphans.Count > 0)
            {
                // Highlight các đối tượng mồ côi
                HighlightObjects(orphans, 1); // Màu đỏ
                WriteError($"\n[CẢNH BÁO] Phát hiện {orphans.Count} phần tử MẤT CHA (Cha đã bị xóa)!");
                WriteMessage("Các phần tử này đang được tô đỏ.");

                // Hỏi người dùng cách xử lý
                var pko = new PromptKeywordOptions("\nBạn muốn xử lý thế nào? [Unlink/ReLink/Ignore]: ");
                pko.Keywords.Add("Unlink");   // Xóa link cũ
                pko.Keywords.Add("ReLink");   // Chọn cha mới ngay
                pko.Keywords.Add("Ignore");   // Để yên đó
                pko.Keywords.Default = "Ignore";

                var res = Ed.GetKeywords(pko);

                if (res.Status == PromptStatus.OK)
                {
                if (res.StringResult == "Unlink")
                    {
                        BreakLinks(orphans);
                    }
                    else if (res.StringResult == "ReLink")
                    {
                        WriteMessage("\nChọn Cha mới cho các phần tử mồ côi này:");
                        // Allow picking any object, but validate it's a DTS element (has DTS data)
                        PromptEntityOptions peo = new PromptEntityOptions("\nChọn Cha mới: ");
                        var per = Ed.GetEntity(peo);

                        if (per.Status == PromptStatus.OK)
                        {
                            // Validate selected parent has DTS data (StoryData or ElementData)
                            bool validParent = false;
                            UsingTransaction(tr =>
                            {
                                var pObj = SafeGetObject(tr, per.ObjectId, OpenMode.ForRead);
                                if (pObj != null && (XDataUtils.ReadStoryData(pObj) != null || XDataUtils.ReadElementData(pObj) != null))
                                    validParent = true;
                            });

                            if (!validParent)
                            {
                                WriteError("Chọn sai loại đối tượng. Vui lòng chọn một phần tử có dữ liệu DTS (Origin hoặc Element).");
                            }
                            else
                            {
                                ExecuteLink(orphans, per.ObjectId, false);
                            }
                        }
                    }
                    else
                    {
                        WriteMessage("Đã bỏ qua. Link lỗi vẫn tồn tại.");
                    }
                }
            }
            else if (validLinks.Count == 0)
            {
                WriteMessage("\nKhông tìm thấy liên kết nào trong các đối tượng đã chọn.");
            }
        }

        #endregion

        #region 4. UNLINK (Cập nhật báo cáo rõ ràng)

        [CommandMethod("DTS_UNLINK")]
        public void DTS_UNLINK()
        {
            WriteMessage("\n=== HỦY LIÊN KẾT ===");
            var ids = AcadUtils.SelectObjectsOnScreen("LINE,LWPOLYLINE,POLYLINE,CIRCLE");
            if (ids.Count == 0) return;

            int success = 0, ignored = 0, protectedOrigins = 0;
            List<ObjectId> toRemoveVisuals = new List<ObjectId>();

            UsingTransaction(tr =>
            {
                foreach (ObjectId id in ids)
                {
                    if (id.IsErased) continue;
                    DBObject obj = SafeGetObject(tr, id, OpenMode.ForWrite);
                    if (obj == null) continue;

                    // Bảo vệ Origin
                    if (XDataUtils.ReadStoryData(obj) != null)
                    {
                        protectedOrigins++;
                        continue;
                    }

                    var elemData = XDataUtils.ReadElementData(obj);
                    if (elemData != null && elemData.IsLinked)
                    {
                        XDataUtils.RemoveLinkTwoWay(obj, tr);
                        success++;
                        toRemoveVisuals.Add(id);
                    }
                    else
                    {
                        ignored++;
                    }
                }
            });

            // Xóa đường visual
            if (toRemoveVisuals.Count > 0) RemoveVisualLines(toRemoveVisuals);

            WriteSuccess($"Đã Unlink: {success} phần tử.");
            if (ignored > 0) WriteMessage($"Bỏ qua: {ignored} phần tử (không có link).");
            if (protectedOrigins > 0) WriteMessage($"Bảo vệ: {protectedOrigins} phần tử là Origin (không thể unlink chính nó).");
        }

        #endregion

        #region Helpers An Toàn (Safety Helpers)

        /// <summary>
        /// Lấy đối tượng an toàn, trả về null nếu lỗi hoặc bị xóa
        /// </summary>
        private DBObject SafeGetObject(Transaction tr, ObjectId id, OpenMode mode)
        {
            if (id == ObjectId.Null || id.IsErased) return null;
            try { return tr.GetObject(id, mode); }
            catch { return null; }
        }

        /// <summary>
        /// Kiểm tra ObjectId có trỏ đến đối tượng hợp lệ không
        /// </summary>
        private bool IsValidObject(Transaction tr, ObjectId id)
        {
            return SafeGetObject(tr, id, OpenMode.ForRead) != null;
        }

        private void CleanUpGhostChildren(Transaction tr, ObjectId parentId)
        {
            try
            {
                DBObject parentObj = tr.GetObject(parentId, OpenMode.ForWrite);
                var story = XDataUtils.ReadStoryData(parentObj);
                var elem = XDataUtils.ReadElementData(parentObj);

                List<string> handles = story != null ? story.ChildHandles : elem?.ChildHandles;
                if (handles == null) return;

                // Lọc giữ lại các handle tồn tại
                var validHandles = new List<string>();
                foreach (string h in handles)
                {
                    ObjectId cid = AcadUtils.GetObjectIdFromHandle(h);
                    if (IsValidObject(tr, cid)) validHandles.Add(h);
                }

                // Ghi lại nếu có thay đổi
                if (validHandles.Count != handles.Count)
                {
                    if (story != null)
                    {
                        story.ChildHandles = validHandles;
                        XDataUtils.WriteStoryData(parentObj, story, tr);
                    }
                    else if (elem != null)
                    {
                        elem.ChildHandles = validHandles;
                        XDataUtils.WriteElementData(parentObj, elem, tr);
                    }
                }
            }
            catch { }
        }

        private void BreakLinks(List<ObjectId> orphans)
        {
            int count = 0;
            UsingTransaction(tr =>
            {
                foreach (ObjectId id in orphans)
                {
                    DBObject obj = SafeGetObject(tr, id, OpenMode.ForWrite);
                    if (obj == null) continue;
                    
                    var data = XDataUtils.ReadElementData(obj);
                    if (data != null)
                    {
                        data.OriginHandle = null; // Cắt link
                        XDataUtils.WriteElementData(obj, data, tr);
                        count++;
                    }
                }
            });
            // Xóa highlight
            HighlightObjects(orphans, 256); // ByLayer
            WriteSuccess($"Đã cắt link (Unlink) cho {count} phần tử.");
        }

        private void HighlightObjects(List<ObjectId> ids, int colorIndex)
        {
            UsingTransaction(tr =>
            {
                foreach (var id in ids)
                {
                    Entity ent = SafeGetObject(tr, id, OpenMode.ForWrite) as Entity;
                    if (ent != null) ent.ColorIndex = colorIndex;
                }
            });
        }

        // UI helper - infer a generic child type string for summary messages
        // UI_PROTECTION_START
        private string InferChildType(Dictionary<ObjectId, List<ObjectId>> validLinks)
        {
            // Try to find first child and read its ElementType to produce a friendly label.
            try
            {
                foreach (var kv in validLinks)
                {
                    if (kv.Value != null && kv.Value.Count > 0)
                    {
                        using (var tr = AcadUtils.Db.TransactionManager.StartTransaction())
                        {
                            var firstChildObj = SafeGetObject(tr, kv.Value[0], OpenMode.ForRead);
                            if (firstChildObj != null)
                            {
                                var childData = XDataUtils.ReadElementData(firstChildObj);
                                if (childData != null)
                                {
                                    tr.Commit();
                                    return childData.ElementType == ElementType.Unknown ? "Phần tử" : childData.ElementType.ToString();
                                }
                            }
                            tr.Commit();
                        }
                    }
                }
            }
            catch { }
            return "Phần tử";
        }
        // UI_PROTECTION_END

        // --- Visual Helpers ---
        private void DrawLinkLines(ObjectId parentId, List<ObjectId> childIds, int colorIndex = 2)
        {
            AcadUtils.CreateLayer("dts_linkmap", 2);
            UsingTransaction(tr =>
            {
                Entity pEnt = SafeGetObject(tr, parentId, OpenMode.ForRead) as Entity;
                if (pEnt == null) return;
                var pCen = AcadUtils.GetEntityCenter(pEnt);

                var btr = (BlockTableRecord)tr.GetObject(pEnt.Database.CurrentSpaceId, OpenMode.ForWrite);

                foreach (var cid in childIds)
                {
                    Entity cEnt = SafeGetObject(tr, cid, OpenMode.ForRead) as Entity;
                    if (cEnt == null) continue;
                    var cCen = AcadUtils.GetEntityCenter(cEnt);

                    // Vẽ đường ảo
                    Line linkLine = new Line(new Autodesk.AutoCAD.Geometry.Point3d(pCen.X, pCen.Y, 0),
                                             new Autodesk.AutoCAD.Geometry.Point3d(cCen.X, cCen.Y, 0));
                    linkLine.Layer = "dts_linkmap";
                    linkLine.ColorIndex = colorIndex; // color per reason
                    btr.AppendEntity(linkLine);
                    tr.AddNewlyCreatedDBObject(linkLine, true);
                }
            });
        }

        private void RemoveVisualLines(List<ObjectId> relatedIds)
        {
            // Đơn giản hóa: Xóa toàn bộ layer dts_linkmap để vẽ lại sau (tránh phức tạp hình học)
            // Hoặc có thể lọc kỹ hơn nếu cần. Ở đây ta chọn giải pháp Clean & Redraw.
            AcadUtils.ClearLayer("dts_linkmap"); 
        }

        #endregion
    }
}
