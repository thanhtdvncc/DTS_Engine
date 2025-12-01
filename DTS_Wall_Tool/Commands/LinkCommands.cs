using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using DTS_Wall_Tool.Core.Data;
using DTS_Wall_Tool.Core.Primitives;
using DTS_Wall_Tool.Core.Utils;

namespace DTS_Wall_Tool.Commands
{
    public class LinkCommands : CommandBase
    {
        private const string LINK_LAYER = "dts_linkmap";
        private const string HIGHLIGHT_LAYER = "dts_highlight";

        // --- LỆNH 2: LIÊN KẾT TƯỜNG VỚI ORIGIN ---
        [CommandMethod("DTS_LINK", CommandFlags.UsePickSet)]
        public void DTS_LINK()
        {
            WriteMessage("\n=== LIÊN KẾT TƯỜNG VỚI ORIGIN ===");

            // 1. Quét chọn hỗn hợp (Đã tự lọc rác trong AcadUtils)
            var selection = AcadUtils.SelectObjectsOnScreen("LINE,CIRCLE");
            if (selection.Count == 0) return;

            UsingTransaction(tr =>
            {
                AcadUtils.CreateLayer(LINK_LAYER, 2); // Layer Vàng

                // 2. Tìm Gốc (Mẹ)
                string originHandle = "";
                string originInfo = "";
                Point2D originCenter = Point2D.Origin;
                ObjectId originId = ObjectId.Null;

                foreach (ObjectId id in selection)
                {
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    StoryData sData = XDataUtils.ReadStoryData(obj, tr);
                    if (sData != null)
                    {
                        originHandle = id.Handle.ToString();
                        originInfo = sData.StoryName;
                        originCenter = AcadUtils.GetEntityCenter(obj as Entity);
                        originId = id;
                        break; // Chỉ cần 1 gốc
                    }
                }

                if (string.IsNullOrEmpty(originHandle))
                {
                    WriteError("Không tìm thấy Gốc (Circle có dữ liệu) trong vùng chọn!");
                    return;
                }

                // 3. Link các Tường (Con)
                int count = 0;
                List<string> childHandles = new List<string>();

                foreach (ObjectId id in selection)
                {
                    if (id == originId) continue;

                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    if (obj is Line line)
                    {
                        // Chỉ link những tường đã có dữ liệu (đã gán DTS_SET)
                        WallData wData = XDataUtils.ReadWallData(obj, tr);
                        if (wData == null) continue;

                        // Ghi dữ liệu
                        obj.UpgradeOpen();
                        wData.OriginHandle = originHandle;
                        XDataUtils.SaveWallData(obj, wData, tr);

                        // Cập nhật danh sách con
                        childHandles.Add(id.Handle.ToString());

                        // Vẽ dây vàng NGAY LẬP TỨC
                        Point2D lineCenter = AcadUtils.GetEntityCenter(line);
                        AcadUtils.CreateLine(lineCenter, originCenter, LINK_LAYER, 2, tr);

                        count++;
                    }
                }

                // 4. Cập nhật danh sách con vào Gốc
                if (childHandles.Count > 0)
                {
                    DBObject originObj = tr.GetObject(originId, OpenMode.ForWrite);
                    var updates = new Dictionary<string, object>();
                    updates["xChildHandles"] = childHandles;
                    XDataUtils.UpdateData(originObj, updates, tr);
                }

                WriteSuccess($"Đã liên kết {count} tường vào {originInfo}.");
            });
        }

        // --- LỆNH 3: HIỂN THỊ LIÊN KẾT (2 CHIỀU & CHỐNG LẶP) ---
        [CommandMethod("DTS_SHOW_LINK", CommandFlags.UsePickSet)]
        public void DTS_SHOW_LINK()
        {
            WriteMessage("\n=== HIỂN THỊ LIÊN KẾT ===");

            var selection = AcadUtils.SelectObjectsOnScreen("LINE,CIRCLE");
            if (selection.Count == 0) return;

            // Xóa cũ để vẽ mới cho sạch (Tránh vẽ đè nhiều lớp)
            AcadUtils.ClearLayer(LINK_LAYER);
            AcadUtils.ClearLayer(HIGHLIGHT_LAYER);

            UsingTransaction(tr =>
            {
                AcadUtils.CreateLayer(LINK_LAYER, 2);       // Vàng
                AcadUtils.CreateLayer(HIGHLIGHT_LAYER, 6);  // Tím

                int linkCount = 0;

                // [BỘ NHỚ ĐỆM] Kiểm soát vẽ trùng lặp
                // Key = "HandleCon_HandleMe"
                HashSet<string> drawnLinks = new HashSet<string>();
                HashSet<string> highlightedOrigins = new HashSet<string>();

                foreach (ObjectId id in selection)
                {
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    Entity ent = obj as Entity;
                    if (ent == null) continue;

                    // --- TRƯỜNG HỢP A: CHỌN TƯỜNG (CON) ---
                    WallData wData = XDataUtils.ReadWallData(obj, tr);
                    if (wData != null && !string.IsNullOrEmpty(wData.OriginHandle))
                    {
                        ObjectId originId = AcadUtils.GetObjectIdFromHandle(wData.OriginHandle);
                        // Vẽ dây về mẹ
                        if (TryDrawLink(ent, originId, tr, drawnLinks))
                        {
                            linkCount++;
                            HighlightParent(originId, tr, highlightedOrigins);
                        }
                    }

                    // --- TRƯỜNG HỢP B: CHỌN GỐC (MẸ) ---
                    StoryData sData = XDataUtils.ReadStoryData(obj, tr);
                    if (sData != null)
                    {
                        // Highlight chính nó
                        HighlightParent(id, tr, highlightedOrigins);

                        // Tìm tất cả các con (Quét toàn bộ bản vẽ để tìm đứa nào thuộc về mình)
                        // Mẹo: Lọc nhanh bằng XData AppName để đỡ tốn tài nguyên
                        var allLines = AcadUtils.SelectAll("LINE");
                        string parentHandle = id.Handle.ToString();

                        foreach (ObjectId childId in allLines)
                        {
                            DBObject childObj = tr.GetObject(childId, OpenMode.ForRead);
                            WallData childData = XDataUtils.ReadWallData(childObj, tr);

                            // Nếu đứa này là con của Mẹ đang chọn -> Vẽ dây
                            if (childData != null && childData.OriginHandle == parentHandle)
                            {
                                // Gọi hàm vẽ (Nó sẽ tự bỏ qua nếu đã vẽ ở bước A rồi)
                                if (TryDrawLink(childObj as Entity, id, tr, drawnLinks))
                                {
                                    linkCount++;
                                }
                            }
                        }
                    }
                }
                WriteSuccess($"Đã hiển thị {linkCount} đường liên kết.");
            });
        }

        [CommandMethod("DTS_CLEAR_LINK")]
        public void DTS_CLEAR_LINK()
        {
            AcadUtils.ClearLayer(LINK_LAYER);
            AcadUtils.ClearLayer(HIGHLIGHT_LAYER);
            WriteSuccess("Đã dọn sạch hiển thị.");
        }

        [CommandMethod("DTS_BREAK_LINK", CommandFlags.UsePickSet)]
        public void DTS_BREAK_LINK()
        {
            WriteMessage("\n=== XÓA LIÊN KẾT ===");
            var lineIds = AcadUtils.SelectObjectsOnScreen("LINE");
            if (lineIds.Count == 0) return;

            UsingTransaction(tr =>
            {
                int broken = 0;
                foreach (ObjectId lineId in lineIds)
                {
                    DBObject obj = tr.GetObject(lineId, OpenMode.ForWrite);
                    WallData wData = XDataUtils.ReadWallData(obj, tr);

                    if (wData != null && !string.IsNullOrEmpty(wData.OriginHandle))
                    {
                        wData.OriginHandle = null; // Xóa handle
                        XDataUtils.SaveWallData(obj, wData, tr);
                        broken++;
                    }
                }
                WriteSuccess($"Đã xóa liên kết của {broken} tường.");
            });

            // Xóa hình ảnh cũ để tránh hiểu nhầm
            DTS_CLEAR_LINK();
        }

        #region Helpers

        // Hàm vẽ thông minh: Kiểm tra trùng lặp trước khi vẽ
        private bool TryDrawLink(Entity child, ObjectId parentId, Transaction tr, HashSet<string> drawnSet)
        {
            if (parentId == ObjectId.Null || parentId.IsErased) return false;

            string parentHandle = parentId.Handle.ToString();
            string childHandle = child.Handle.ToString();
            string key = $"{childHandle}_{parentHandle}";

            // Nếu đã vẽ cặp này rồi -> Bỏ qua
            if (drawnSet.Contains(key)) return false;

            Entity parent = tr.GetObject(parentId, OpenMode.ForRead) as Entity;
            Point2D pStart = AcadUtils.GetEntityCenter(child);
            Point2D pEnd = AcadUtils.GetEntityCenter(parent);

            AcadUtils.CreateLine(pStart, pEnd, LINK_LAYER, 2, tr);

            drawnSet.Add(key); // Đánh dấu đã vẽ
            return true;
        }

        private void HighlightParent(ObjectId parentId, Transaction tr, HashSet<string> highlightedSet)
        {
            if (parentId == ObjectId.Null || parentId.IsErased) return;

            string key = parentId.Handle.ToString();
            if (highlightedSet.Contains(key)) return; // Đã highlight rồi

            Entity parent = tr.GetObject(parentId, OpenMode.ForRead) as Entity;
            Point2D center = AcadUtils.GetEntityCenter(parent);

            // Vẽ vòng tròn tím (Màu 6)
            AcadUtils.CreateCircle(center, 600, HIGHLIGHT_LAYER, 6, tr);

            highlightedSet.Add(key);
        }

        #endregion
    }
}