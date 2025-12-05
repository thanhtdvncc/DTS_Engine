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
    /// Quản lý liên kết phần tử (Smart Linking System).
    /// Hỗ trợ liên kết Cha-Con và Reference (nhánh phụ).
    /// Sử dụng VisualUtils để hiển thị tạm thời, không làm bẩn bản vẽ.
    /// </summary>
    public class LinkCommands : CommandBase
    {
        #region 1. DTS_LINK_ORIGIN (Gán phần tử vào Story/Trục)

        /// <summary>
        /// Liên kết các phần tử với Story Origin.
        /// Quét chọn vùng chứa Origin và các phần tử cần liên kết.
        /// </summary>
        [CommandMethod("DTS_LINK_ORIGIN")]
        public void DTS_LINK_ORIGIN()
        {
            WriteMessage("\n=== LIÊN KẾT VỚI ORIGIN (STORY) ===");
            
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
                    
                    DBObject obj = SafeGetObject(tr, id, OpenMode.ForRead);
                    if (obj == null) continue;

                    if (XDataUtils.ReadStoryData(obj) != null)
                        originId = id;
                    else if (XDataUtils.ReadElementData(obj) != null)
                        childIds.Add(id);
                }
            });

            if (originId == ObjectId.Null)
            {
                WriteError("Không tìm thấy Origin nào trong vùng chọn.");
                return;
            }

            if (childIds.Count == 0)
            {
                WriteMessage("Không có phần tử con nào để liên kết.");
                return;
            }

            ExecuteSmartLink(childIds, originId, isStoryOrigin: true);
        }

        #endregion

        #region 2. DTS_LINK (Liên kết Cha - Con kết cấu)

        /// <summary>
        /// Tạo liên kết Cha - Con.
        /// Logic mới: Nếu đã có Cha chính, tự động thêm vào Reference (nhánh phụ).
        /// </summary>
        [CommandMethod("DTS_LINK")]
        public void DTS_LINK()
        {
            WriteMessage("\n=== THIẾT LẬP LIÊN KẾT PHẦN TỬ ===");

            // Dọn dẹp visual cũ
            VisualUtils.ClearAll();

            // Bước 1: Chọn Con
            WriteMessage("\n1. Chọn các phần tử CON cần liên kết:");
            var childIds = AcadUtils.SelectObjectsOnScreen("LINE,LWPOLYLINE,POLYLINE,CIRCLE");
            if (childIds.Count == 0) return;

            WriteMessage($"   Đã chọn {childIds.Count} phần tử con.");

            // Bước 2: Chọn Cha
            PromptEntityOptions peo = new PromptEntityOptions("\n2. Chọn phần tử CHA (Origin, Dầm, Cột...):");
            var per = Ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            ObjectId parentId = per.ObjectId;

            if (childIds.Contains(parentId))
            {
                childIds.Remove(parentId);
                WriteMessage("   (Đã loại bỏ đối tượng Cha khỏi danh sách Con)");
            }

            ExecuteSmartLink(childIds, parentId, isStoryOrigin: false);
        }

        #endregion

        #region Core Smart Linking Logic

        /// <summary>
        /// Thực hiện liên kết thông minh.
        /// Nếu phần tử đã có Cha chính và đang liên kết với Cha khác,
        /// sẽ tự động thêm vào Reference (nhánh phụ) thay vì ghi đè.
        /// </summary>
        private void ExecuteSmartLink(List<ObjectId> childIds, ObjectId parentId, bool isStoryOrigin)
        {
            int primaryCount = 0, refCount = 0, errorCount = 0, cycleCount = 0;
            var newlyLinked = new List<ObjectId>();
            var skippedByReason = new Dictionary<string, List<ObjectId>>();
            string parentInfo = parentId.Handle.ToString();
            string parentHandle = parentId.Handle.ToString();

            // Highlight Cha để user dễ nhìn
            VisualUtils.ClearAll();
            VisualUtils.HighlightObject(parentId, 4); // Cyan

            UsingTransaction(tr =>
            {
                DBObject parentObj = SafeGetObject(tr, parentId, OpenMode.ForWrite);
                if (parentObj == null) return;

                // Xác định Parent Type & Data
                ElementType parentType = ElementType.Unknown;
                var storyData = XDataUtils.ReadStoryData(parentObj);
                var parentElemData = XDataUtils.ReadElementData(parentObj);

                if (storyData != null)
                {
                    parentType = ElementType.StoryOrigin;
                    parentInfo = $"Origin {storyData.StoryName} ({storyData.Elevation:0}mm)";
                }
                else if (parentElemData != null)
                {
                    parentType = parentElemData.ElementType;
                    parentInfo = $"{parentElemData.ElementType} (handle {parentHandle})";
                }
                else
                {
                    // Tự động gán StoryData nếu chưa có dữ liệu DTS
                    var autoOrigin = new StoryData { StoryName = "AutoOrigin", Elevation = 0 };
                    XDataUtils.WriteStoryData(parentObj, autoOrigin, tr);
                    storyData = autoOrigin;
                    parentType = ElementType.StoryOrigin;
                    parentInfo = "Origin AutoOrigin (0mm)";
                }

                foreach (ObjectId childId in childIds)
                {
                    Entity childEnt = SafeGetObject(tr, childId, OpenMode.ForWrite) as Entity;
                    if (childEnt == null) continue;

                    var childData = XDataUtils.ReadElementData(childEnt);
                    if (childData == null)
                    {
                        AddToSkipList(skippedByReason, "NoData", childId);
                        errorCount++;
                        continue;
                    }

                    // Rule 1: Hierarchy Check
                    if (!LinkRules.CanBePrimaryParent(parentType, childData.ElementType))
                    {
                        AddToSkipList(skippedByReason, $"Hierarchy:{childData.ElementType}->{parentType}", childId);
                        errorCount++;
                        continue;
                    }

                    // Rule 2: Acyclic Check (Chỉ áp dụng nếu cha không phải là Story)
                    if (!isStoryOrigin && LinkRules.DetectCycle(parentObj, childEnt.Handle.ToString(), tr))
                    {
                        AddToSkipList(skippedByReason, "Cycle", childId);
                        cycleCount++;
                        continue;
                    }

                    // Quyết định loại Link: Primary hoặc Reference
                    bool isReference = false;

                    if (!string.IsNullOrEmpty(childData.OriginHandle))
                    {
                        if (childData.OriginHandle != parentHandle)
                        {
                            // Đã có cha khác -> Thêm vào Reference (nhánh phụ)
                            if (childData.ReferenceHandles == null)
                                childData.ReferenceHandles = new List<string>();

                            if (!childData.ReferenceHandles.Contains(parentHandle))
                            {
                                childData.ReferenceHandles.Add(parentHandle);
                                isReference = true;
                                refCount++;
                            }
                        }
                        // Nếu đã là cha hiện tại -> bỏ qua (không làm gì)
                    }
                    else
                    {
                        // Chưa có cha -> Gán làm cha chính
                        childData.OriginHandle = parentHandle;

                        // Kế thừa cao độ nếu cha là Story
                        if (storyData != null)
                        {
                            childData.BaseZ = storyData.Elevation;
                            childData.Height = storyData.StoryHeight;
                        }

                        primaryCount++;
                    }

                    // Cập nhật dữ liệu Con
                    XDataUtils.WriteElementData(childEnt, childData, tr);

                    // Cập nhật dữ liệu Cha (thêm handle con vào danh sách)
                    UpdateParentChildList(parentObj, childEnt.Handle.ToString(), storyData, parentElemData, tr);

                    newlyLinked.Add(childId);
                }

                // Lưu lại Cha
                if (storyData != null)
                    XDataUtils.WriteStoryData(parentObj, storyData, tr);
                else if (parentElemData != null)
                    XDataUtils.WriteElementData(parentObj, parentElemData, tr);
            });

            // Hiển thị visual cho các phần tử đã link
            if (newlyLinked.Count > 0)
            {
                VisualUtils.DrawLinkLines(parentId, newlyLinked, 3); // Green
                VisualUtils.HighlightObjects(newlyLinked, 3);
            }

            // Hiển thị visual cho các phần tử bị bỏ qua
            foreach (var kv in skippedByReason)
            {
                int colorIndex = kv.Key.StartsWith("Hierarchy") ? 1 : (kv.Key == "Cycle" ? 6 : 4);
                VisualUtils.HighlightObjects(kv.Value, colorIndex);
            }

            // Báo cáo kết quả
            WriteSuccess($"Kết quả liên kết với [{parentInfo}]:");
            if (primaryCount > 0) WriteMessage($"  - Liên kết chính (Primary): {primaryCount} phần tử");
            if (refCount > 0) WriteMessage($"  - Liên kết nhánh (Reference): {refCount} phần tử");

            // Báo cáo lỗi
            if (skippedByReason.Count > 0)
            {
                foreach (var kv in skippedByReason)
                {
                    string reason = kv.Key;
                    int count = kv.Value.Count;

                    if (reason.StartsWith("Hierarchy:"))
                    {
                        string detail = reason.Replace("Hierarchy:", "");
                        WriteMessage($"  - Bỏ qua {count} phần tử (phân cấp không hợp lệ: {detail})");
                    }
                    else if (reason == "Cycle")
                    {
                        WriteMessage($"  - Bỏ qua {count} phần tử (tham chiếu vòng)");
                    }
                    else if (reason == "NoData")
                    {
                        WriteMessage($"  - Bỏ qua {count} phần tử (chưa có dữ liệu DTS)");
                    }
                }
            }

            if (cycleCount > 0)
                WriteWarning($"Phát hiện {cycleCount} trường hợp tham chiếu vòng.");

            WriteMessage("\n(Sử dụng DTS_CLEAR_VISUAL để xóa hiển thị tạm thời)");
        }

        private void UpdateParentChildList(DBObject parentObj, string childHandle, StoryData storyData, ElementData parentElemData, Transaction tr)
        {
            if (storyData != null)
            {
                if (!storyData.ChildHandles.Contains(childHandle))
                    storyData.ChildHandles.Add(childHandle);
            }
            else if (parentElemData != null)
            {
                if (!parentElemData.ChildHandles.Contains(childHandle))
                    parentElemData.ChildHandles.Add(childHandle);
            }
        }

        private void AddToSkipList(Dictionary<string, List<ObjectId>> dict, string reason, ObjectId id)
        {
            if (!dict.ContainsKey(reason))
                dict[reason] = new List<ObjectId>();
            dict[reason].Add(id);
        }

        #endregion

        #region 3. DTS_SHOW_LINK (Hiển thị liên kết & Audit)

        /// <summary>
        /// Hiển thị các liên kết và kiểm tra tính toàn vẹn.
        /// Tự động phát hiện và xử lý: Con mất cha (Orphan), Cha chứa con đã xóa (Ghost).
        /// </summary>
        [CommandMethod("DTS_SHOW_LINK")]
        public void DTS_SHOW_LINK()
        {
            WriteMessage("\n=== HIỂN THỊ LIÊN KẾT & KIỂM TRA ===");

            var ids = AcadUtils.SelectObjectsOnScreen("LINE,LWPOLYLINE,POLYLINE,CIRCLE");
            if (ids.Count == 0) return;

            // Dọn dẹp visual cũ
            VisualUtils.ClearAll();

            var orphans = new List<ObjectId>();
            var ghostRefs = new Dictionary<ObjectId, int>();
            var validLinks = new Dictionary<ObjectId, List<ObjectId>>();

            UsingTransaction(tr =>
            {
                foreach (ObjectId id in ids)
                {
                    if (id.IsErased) continue;
                    DBObject obj = SafeGetObject(tr, id, OpenMode.ForRead);
                    if (obj == null) continue;

                    // Kiểm tra vai trò là Con
                    var elemData = XDataUtils.ReadElementData(obj);
                    if (elemData != null && elemData.IsLinked)
                    {
                        ObjectId parentId = AcadUtils.GetObjectIdFromHandle(elemData.OriginHandle);

                        if (IsValidObject(tr, parentId))
                        {
                            if (!validLinks.ContainsKey(parentId))
                                validLinks[parentId] = new List<ObjectId>();
                            validLinks[parentId].Add(id);
                        }
                        else
                        {
                            orphans.Add(id);
                        }
                    }

                    // Kiểm tra vai trò là Cha
                    List<string> childHandles = null;
                    var storyData = XDataUtils.ReadStoryData(obj);
                    if (storyData != null)
                        childHandles = storyData.ChildHandles;
                    else if (elemData != null)
                        childHandles = elemData.ChildHandles;

                    if (childHandles != null && childHandles.Count > 0)
                    {
                        int ghosts = 0;
                        foreach (string h in childHandles)
                        {
                            ObjectId cId = AcadUtils.GetObjectIdFromHandle(h);
                            if (!IsValidObject(tr, cId))
                            {
                                ghosts++;
                            }
                            else
                            {
                                if (!validLinks.ContainsKey(id))
                                    validLinks[id] = new List<ObjectId>();
                                if (!validLinks[id].Contains(cId))
                                    validLinks[id].Add(cId);
                            }
                        }
                        if (ghosts > 0) ghostRefs[id] = ghosts;
                    }
                }
            });

            // Vẽ link hợp lệ
            if (validLinks.Count > 0)
            {
                int totalLinks = 0;
                foreach (var kv in validLinks)
                {
                    VisualUtils.DrawLinkLines(kv.Key, kv.Value, 3); // Green
                    totalLinks += kv.Value.Count;
                }
                WriteSuccess($"Hiển thị {totalLinks} liên kết hợp lệ.");
            }

            // Dọn dẹp "con ma" (Ghost Children)
            if (ghostRefs.Count > 0)
            {
                int totalGhosts = ghostRefs.Values.Sum();
                WriteMessage($"\nĐang dọn dẹp {totalGhosts} tham chiếu rác (con đã bị xóa)...");

                UsingTransaction(tr =>
                {
                    foreach (var kv in ghostRefs)
                    {
                        CleanUpGhostChildren(tr, kv.Key);
                    }
                });
                WriteSuccess("Đã làm sạch dữ liệu Cha.");
            }

            // Xử lý "mồ côi" (Orphans)
            if (orphans.Count > 0)
            {
                VisualUtils.HighlightObjects(orphans, 1); // Red
                WriteWarning($"Phát hiện {orphans.Count} phần tử MẤT CHA (Cha đã bị xóa).");

                var pko = new PromptKeywordOptions("\nChọn cách xử lý: [Unlink/ReLink/Ignore]: ");
                pko.Keywords.Add("Unlink");
                pko.Keywords.Add("ReLink");
                pko.Keywords.Add("Ignore");
                pko.Keywords.Default = "Ignore";

                var res = Ed.GetKeywords(pko);

                if (res.Status == PromptStatus.OK)
                {
                    if (res.StringResult == "Unlink")
                    {
                        BreakOrphanLinks(orphans);
                    }
                    else if (res.StringResult == "ReLink")
                    {
                        WriteMessage("\nChọn Cha mới cho các phần tử mồ côi:");
                        PromptEntityOptions peo = new PromptEntityOptions("\nChọn Cha mới: ");
                        var per = Ed.GetEntity(peo);

                        if (per.Status == PromptStatus.OK)
                        {
                            bool validParent = false;
                            UsingTransaction(tr =>
                            {
                                var pObj = SafeGetObject(tr, per.ObjectId, OpenMode.ForRead);
                                if (pObj != null && (XDataUtils.ReadStoryData(pObj) != null || XDataUtils.ReadElementData(pObj) != null))
                                    validParent = true;
                            });

                            if (!validParent)
                            {
                                WriteError("Đối tượng được chọn không có dữ liệu DTS.");
                            }
                            else
                            {
                                ExecuteSmartLink(orphans, per.ObjectId, false);
                            }
                        }
                    }
                    else
                    {
                        WriteMessage("Đã bỏ qua. Liên kết lỗi vẫn tồn tại.");
                    }
                }
            }
            else if (validLinks.Count == 0)
            {
                WriteMessage("Không tìm thấy liên kết nào trong các đối tượng đã chọn.");
            }

            WriteMessage("\n(Sử dụng DTS_CLEAR_VISUAL để xóa hiển thị tạm thời)");
        }

        #endregion

        #region 4. DTS_UNLINK (Gỡ liên kết cụ thể)

        /// <summary>
        /// Gỡ liên kết cụ thể giữa Con và Cha.
        /// Nếu gỡ Cha chính, sẽ tự động đôn Reference đầu tiên lên làm Cha chính.
        /// </summary>
        [CommandMethod("DTS_UNLINK")]
        public void DTS_UNLINK()
        {
            WriteMessage("\n=== GỠ LIÊN KẾT CỤ THỂ ===");

            VisualUtils.ClearAll();

            // Bước 1: Chọn Con
            PromptEntityOptions peoChild = new PromptEntityOptions("\n1. Chọn phần tử CON cần gỡ liên kết:");
            var resChild = Ed.GetEntity(peoChild);
            if (resChild.Status != PromptStatus.OK) return;

            VisualUtils.HighlightObject(resChild.ObjectId, 6); // Magenta

            // Bước 2: Xác định các cha hiện tại
            var parents = new List<ObjectId>();
            UsingTransaction(tr =>
            {
                var childData = XDataUtils.ReadElementData(tr.GetObject(resChild.ObjectId, OpenMode.ForRead));
                if (childData != null)
                {
                    if (!string.IsNullOrEmpty(childData.OriginHandle))
                    {
                        var pid = AcadUtils.GetObjectIdFromHandle(childData.OriginHandle);
                        if (pid != ObjectId.Null) parents.Add(pid);
                    }

                    if (childData.ReferenceHandles != null)
                    {
                        foreach (var refH in childData.ReferenceHandles)
                        {
                            var rid = AcadUtils.GetObjectIdFromHandle(refH);
                            if (rid != ObjectId.Null) parents.Add(rid);
                        }
                    }
                }
            });

            if (parents.Count == 0)
            {
                WriteMessage("Phần tử này chưa có liên kết nào.");
                VisualUtils.ClearAll();
                return;
            }

            // Highlight các cha
            VisualUtils.HighlightObjects(parents, 2); // Yellow
            WriteMessage($"Phần tử đang liên kết với {parents.Count} đối tượng (đang tô vàng).");

            // Bước 3: Chọn Cha cần gỡ
            PromptEntityOptions peoParent = new PromptEntityOptions("\n2. Chọn đối tượng CHA muốn gỡ bỏ:");
            var resParent = Ed.GetEntity(peoParent);

            if (resParent.Status == PromptStatus.OK)
            {
                if (ExecuteUnlinkSpecific(resChild.ObjectId, resParent.ObjectId))
                {
                    WriteSuccess("Đã gỡ liên kết thành công.");
                }
                else
                {
                    WriteError("Không tìm thấy liên kết giữa 2 đối tượng này.");
                }
            }

            VisualUtils.ClearAll();
        }

        private bool ExecuteUnlinkSpecific(ObjectId childId, ObjectId parentId)
        {
            bool result = false;

            UsingTransaction(tr =>
            {
                var childObj = tr.GetObject(childId, OpenMode.ForWrite);
                var childData = XDataUtils.ReadElementData(childObj);
                string parentHandle = parentId.Handle.ToString();

                if (childData != null)
                {
                    // Case A: Gỡ Cha chính
                    if (childData.OriginHandle == parentHandle)
                    {
                        childData.OriginHandle = null;

                        // Đôn Reference đầu tiên lên làm Cha chính (nếu có)
                        if (childData.ReferenceHandles != null && childData.ReferenceHandles.Count > 0)
                        {
                            childData.OriginHandle = childData.ReferenceHandles[0];
                            childData.ReferenceHandles.RemoveAt(0);
                            WriteMessage($"Đã chuyển {childData.OriginHandle} thành Cha chính.");
                        }
                        result = true;
                    }
                    // Case B: Gỡ Reference
                    else if (childData.ReferenceHandles != null && childData.ReferenceHandles.Contains(parentHandle))
                    {
                        childData.ReferenceHandles.Remove(parentHandle);
                        result = true;
                    }

                    if (result)
                    {
                        XDataUtils.WriteElementData(childObj, childData, tr);

                        // Xóa reference ngược từ Cha -> Con
                        RemoveChildFromParent(tr, parentId, childId.Handle.ToString());
                    }
                }
            });

            return result;
        }

        private void RemoveChildFromParent(Transaction tr, ObjectId parentId, string childHandle)
        {
            try
            {
                var parentObj = tr.GetObject(parentId, OpenMode.ForWrite);
                var storyData = XDataUtils.ReadStoryData(parentObj);
                var elemData = XDataUtils.ReadElementData(parentObj);

                if (storyData != null && storyData.ChildHandles.Contains(childHandle))
                {
                    storyData.ChildHandles.Remove(childHandle);
                    XDataUtils.WriteStoryData(parentObj, storyData, tr);
                }
                else if (elemData != null && elemData.ChildHandles.Contains(childHandle))
                {
                    elemData.ChildHandles.Remove(childHandle);
                    XDataUtils.WriteElementData(parentObj, elemData, tr);
                }
            }
            catch { }
        }

        #endregion

        #region 5. DTS_CLEAR_LINK (Xóa toàn bộ liên kết)

        /// <summary>
        /// Xóa sạch mọi liên kết của đối tượng (Reset về trạng thái tự do).
        /// </summary>
        [CommandMethod("DTS_CLEAR_LINK")]
        public void DTS_CLEAR_LINK()
        {
            WriteMessage("\n=== XÓA TOÀN BỘ LIÊN KẾT ===");

            var ids = AcadUtils.SelectObjectsOnScreen("LINE,LWPOLYLINE,POLYLINE,CIRCLE");
            if (ids.Count == 0) return;

            int count = 0;
            int protectedOrigins = 0;

            UsingTransaction(tr =>
            {
                foreach (var id in ids)
                {
                    var obj = SafeGetObject(tr, id, OpenMode.ForWrite);
                    if (obj == null) continue;

                    // Bảo vệ Origin
                    if (XDataUtils.ReadStoryData(obj) != null)
                    {
                        protectedOrigins++;
                        continue;
                    }

                    var data = XDataUtils.ReadElementData(obj);
                    if (data != null && (data.IsLinked || (data.ReferenceHandles != null && data.ReferenceHandles.Count > 0)))
                    {
                        // Xóa 2 chiều
                        XDataUtils.RemoveLinkTwoWay(obj, tr);

                        // Clear Reference
                        data = XDataUtils.ReadElementData(obj); // Reload
                        if (data != null && data.ReferenceHandles != null)
                        {
                            data.ReferenceHandles.Clear();
                            XDataUtils.WriteElementData(obj, data, tr);
                        }

                        count++;
                    }
                }
            });

            WriteSuccess($"Đã xóa liên kết của {count} phần tử.");
            if (protectedOrigins > 0)
                WriteMessage($"Bỏ qua {protectedOrigins} phần tử Origin (không thể xóa liên kết Origin).");
        }

        #endregion

        #region 6. DTS_CLEAR_VISUAL (Dọn dẹp hiển thị tạm)

        /// <summary>
        /// Xóa tất cả hiển thị tạm thời (Transient Graphics).
        /// </summary>
        [CommandMethod("DTS_CLEAR_VISUAL")]
        public void DTS_CLEAR_VISUAL()
        {
            VisualUtils.ClearAll();
            WriteSuccess("Đã xóa hiển thị tạm thời.");
        }

        #endregion

        #region Safety Helpers

        private DBObject SafeGetObject(Transaction tr, ObjectId id, OpenMode mode)
        {
            if (id == ObjectId.Null || id.IsErased) return null;
            try { return tr.GetObject(id, mode); }
            catch { return null; }
        }

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

                var validHandles = new List<string>();
                foreach (string h in handles)
                {
                    ObjectId cid = AcadUtils.GetObjectIdFromHandle(h);
                    if (IsValidObject(tr, cid))
                        validHandles.Add(h);
                }

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

        private void BreakOrphanLinks(List<ObjectId> orphans)
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
                        data.OriginHandle = null;
                        XDataUtils.WriteElementData(obj, data, tr);
                        count++;
                    }
                }
            });

            WriteSuccess($"Đã cắt liên kết cho {count} phần tử mồ côi.");
        }

        #endregion
    }
}
