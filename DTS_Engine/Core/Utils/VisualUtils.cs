using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Geometry;
using DTS_Engine.Core.Data;
using DTS_Engine.Core.Primitives;
using System;
using System.Collections.Generic;

namespace DTS_Engine.Core.Utils
{
    /// <summary>
    /// Qu?n lý hi?n th? t?m th?i (Transient Graphics) ?? tránh làm "b?n" b?n v?.
    /// S? d?ng TransientManager ?? v? overlay màu ?o lên ??i t??ng.
    /// Overlay s? bi?n m?t khi g?i ClearAll() ho?c Regen, tr? l?i nguyên tr?ng b?n v?.
    /// 
    /// Tuân th? ISO/IEC 25010: Maintainability, Non-destructive visualization.
    /// </summary>
    public static class VisualUtils
    {
        private static readonly List<DBObject> _transients = new List<DBObject>();
        private static readonly object _syncLock = new object();

        #region Highlight Objects

        /// <summary>
        /// Highlight ??i t??ng b?ng màu t?m th?i (không ??i màu g?c c?a Entity).
        /// Clone entity và v? overlay v?i màu ch? ??nh.
        /// </summary>
        /// <param name="id">ObjectId c?a entity c?n highlight</param>
        /// <param name="colorIndex">Mã màu AutoCAD (1=Red, 2=Yellow, 3=Green, 4=Cyan, 5=Blue, 6=Magenta, 7=White)</param>
        public static void HighlightObject(ObjectId id, int colorIndex)
        {
            if (id == ObjectId.Null || id.IsErased) return;

            try
            {
                Entity entClone = null;

                AcadUtils.UsingTransaction(tr =>
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null && !ent.IsErased)
                    {
                        entClone = ent.Clone() as Entity;
                        if (entClone != null)
                        {
                            entClone.ColorIndex = colorIndex;
                        }
                    }
                });

                if (entClone != null)
                {
                    AddTransientInternal(entClone, TransientDrawingMode.Highlight);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VisualUtils] HighlightObject error: {ex.Message}");
            }
        }

        /// <summary>
        /// Highlight danh sách ??i t??ng v?i cùng m?t màu.
        /// </summary>
        public static void HighlightObjects(List<ObjectId> ids, int colorIndex)
        {
            if (ids == null || ids.Count == 0) return;

            foreach (var id in ids)
            {
                HighlightObject(id, colorIndex);
            }
        }

        /// <summary>
        /// Highlight ??i t??ng v?i màu d?a trên tr?ng thái ??ng b?.
        /// </summary>
        public static void HighlightBySyncState(ObjectId id, SyncState state)
        {
            int color = GetColorForSyncState(state);
            HighlightObject(id, color);
        }

        #endregion

        #region Draw Transient Geometry

        /// <summary>
        /// V? ???ng Line t?m th?i (không t?o entity th?t trong b?n v?).
        /// </summary>
        public static void DrawTransientLine(Point3d start, Point3d end, int colorIndex)
        {
            try
            {
                var line = new Line(start, end);
                line.ColorIndex = colorIndex;
                AddTransientInternal(line, TransientDrawingMode.Main);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VisualUtils] DrawTransientLine error: {ex.Message}");
            }
        }

        /// <summary>
        /// V? ???ng Line t?m th?i t? Point2D (Z=0).
        /// </summary>
        public static void DrawTransientLine(Point2D start, Point2D end, int colorIndex)
        {
            DrawTransientLine(
                new Point3d(start.X, start.Y, 0),
                new Point3d(end.X, end.Y, 0),
                colorIndex
            );
        }

        /// <summary>
        /// V? vòng tròn t?m th?i.
        /// </summary>
        public static void DrawTransientCircle(Point3d center, double radius, int colorIndex)
        {
            try
            {
                var circle = new Circle(center, Vector3d.ZAxis, radius);
                circle.ColorIndex = colorIndex;
                AddTransientInternal(circle, TransientDrawingMode.Main);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VisualUtils] DrawTransientCircle error: {ex.Message}");
            }
        }

        /// <summary>
        /// Thêm ??i t??ng tùy ý vào danh sách transient ?? hi?n th?.
        /// </summary>
        /// <param name="obj">DBObject ?? hi?n th? (ph?i là Entity)</param>
        /// <param name="colorIndex">Mã màu (256 = ByLayer)</param>
        public static void AddTransient(DBObject obj, int colorIndex = 256)
        {
            if (obj == null) return;

            try
            {
                if (obj is Entity ent)
                {
                    if (colorIndex != 256)
                    {
                        ent.ColorIndex = colorIndex;
                    }
                    AddTransientInternal(ent, TransientDrawingMode.Main);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VisualUtils] AddTransient error: {ex.Message}");
            }
        }

        #endregion

        #region Draw Link Lines

        /// <summary>
        /// V? các ???ng link t?m th?i t? Parent ??n danh sách Children.
        /// Thay th? cho vi?c t?o Line th?t trên layer dts_linkmap.
        /// </summary>
        public static void DrawLinkLines(ObjectId parentId, List<ObjectId> childIds, int colorIndex = 2)
        {
            if (parentId == ObjectId.Null || childIds == null || childIds.Count == 0) return;

            try
            {
                Point3d parentCenter = Point3d.Origin;

                AcadUtils.UsingTransaction(tr =>
                {
                    var parentEnt = tr.GetObject(parentId, OpenMode.ForRead) as Entity;
                    if (parentEnt != null)
                    {
                        parentCenter = AcadUtils.GetEntityCenter3d(parentEnt);
                    }
                });

                if (parentCenter == Point3d.Origin) return;

                foreach (var childId in childIds)
                {
                    if (childId == ObjectId.Null || childId.IsErased) continue;

                    Point3d childCenter = Point3d.Origin;

                    AcadUtils.UsingTransaction(tr =>
                    {
                        var childEnt = tr.GetObject(childId, OpenMode.ForRead) as Entity;
                        if (childEnt != null)
                        {
                            childCenter = AcadUtils.GetEntityCenter3d(childEnt);
                        }
                    });

                    if (childCenter != Point3d.Origin)
                    {
                        DrawTransientLine(parentCenter, childCenter, colorIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VisualUtils] DrawLinkLines error: {ex.Message}");
            }
        }

        #endregion

        #region Scan Link Visualization

        /// <summary>
        /// V? các ???ng scan link t?m th?i t? ?i?m g?c ??n các item.
        /// Thay th? cho DrawScanLinks trong ScanCommands.
        /// </summary>
        public static int DrawScanLinks(Point3d originPt, List<ScanLinkItem> items)
        {
            if (items == null || items.Count == 0) return 0;

            int count = 0;

            foreach (var item in items)
            {
                try
                {
                    var line = new Line(originPt, item.Center);
                    line.ColorIndex = item.ColorIndex;
                    AddTransientInternal(line, TransientDrawingMode.Main);
                    count++;
                }
                catch
                {
                    // Skip failed items
                }
            }

            return count;
        }

        #endregion

        #region Clear & Cleanup

        /// <summary>
        /// Xóa toàn b? các hi?n th? t?m th?i (tr? l?i nguyên tr?ng màn hình).
        /// </summary>
        public static void ClearAll()
        {
            lock (_syncLock)
            {
                try
                {
                    var tm = TransientManager.CurrentTransientManager;

                    foreach (var obj in _transients)
                    {
                        if (obj != null)
                        {
                            try
                            {
                                tm.EraseTransient(obj, new IntegerCollection());
                            }
                            catch
                            {
                                // Ignore erase errors
                            }

                            try
                            {
                                obj.Dispose();
                            }
                            catch
                            {
                                // Ignore dispose errors
                            }
                        }
                    }

                    _transients.Clear();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VisualUtils] ClearAll error: {ex.Message}");
                    _transients.Clear();
                }
            }
        }

        /// <summary>
        /// L?y s? l??ng transient ?ang ???c hi?n th?.
        /// </summary>
        public static int TransientCount
        {
            get
            {
                lock (_syncLock)
                {
                    return _transients.Count;
                }
            }
        }

        #endregion

        #region Internal Helpers

        private static void AddTransientInternal(Entity entity, TransientDrawingMode mode)
        {
            if (entity == null) return;

            lock (_syncLock)
            {
                try
                {
                    var tm = TransientManager.CurrentTransientManager;
                    tm.AddTransient(entity, mode, 128, new IntegerCollection());
                    _transients.Add(entity);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VisualUtils] AddTransientInternal error: {ex.Message}");
                    entity.Dispose();
                }
            }
        }

        private static int GetColorForSyncState(SyncState state)
        {
            switch (state)
            {
                case SyncState.Synced: return 3;        // Green
                case SyncState.CadModified: return 4;   // Cyan
                case SyncState.SapModified: return 5;   // Blue
                case SyncState.Conflict: return 6;      // Magenta
                case SyncState.SapDeleted: return 1;    // Red
                case SyncState.NewElement: return 2;    // Yellow
                default: return 7;                      // White
            }
        }

        #endregion
    }

    #region Supporting Types

    /// <summary>
    /// Item dùng cho scan link visualization.
    /// </summary>
    public class ScanLinkItem
    {
        public ObjectId ObjId { get; set; }
        public Point3d Center { get; set; }
        public int ColorIndex { get; set; }
        public string Type { get; set; }
    }

    #endregion
}
