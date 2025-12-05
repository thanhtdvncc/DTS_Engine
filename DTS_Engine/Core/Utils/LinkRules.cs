using Autodesk.AutoCAD.DatabaseServices;
using DTS_Engine.Core.Data;

namespace DTS_Engine.Core.Utils
{
    /// <summary>
    /// Quy tac lien ket giua cac phan tu trong DTS Engine.
    /// Dam bao tinh toan ven cua cay lien ket.
    /// </summary>
    public static class LinkRules
    {
        /// <summary>
        /// Rule 1: Phan cap nghiem ngat cho Cha Chinh (Primary Parent).
        /// Xac dinh xem mot loai phan tu co the lam cha cua loai khac khong.
        /// </summary>
        public static bool CanBePrimaryParent(ElementType parentType, ElementType childType)
        {
            if (parentType == ElementType.StoryOrigin)
                return childType.IsStructuralElement() || childType == ElementType.ElementOrigin;

            if (parentType.IsStructuralElement())
            {
                if (childType == ElementType.Rebar ||
                    childType == ElementType.Lintel ||
                    childType == ElementType.Stair) return true;

                if (childType.IsStructuralElement()) return true;

                if (childType == ElementType.Unknown) return true;
            }

            return false;
        }

        /// <summary>
        /// Rule 2: Chong vong lap (Acyclic Check).
        /// Duyet nguoc tu Parent len tren, neu gap Child Handle thi la vong lap.
        /// </summary>
        public static bool DetectCycle(DBObject parentObj, string childHandle, Transaction tr)
        {
            if (parentObj == null || string.IsNullOrEmpty(childHandle)) return false;

            string currentHandle = parentObj.Handle.ToString();
            if (currentHandle == childHandle) return true;

            var currentData = XDataUtils.ReadElementData(parentObj);
            if (currentData == null)
            {
                var story = XDataUtils.ReadStoryData(parentObj);
                if (story != null) return false;
            }

            int safetyCounter = 0;
            while (currentData != null && currentData.IsLinked && safetyCounter < 100)
            {
                if (currentData.OriginHandle == childHandle) return true;

                ObjectId parentId = AcadUtils.GetObjectIdFromHandle(currentData.OriginHandle);
                if (parentId == ObjectId.Null) break;

                try
                {
                    var parentEnt = tr.GetObject(parentId, OpenMode.ForRead);
                    currentData = XDataUtils.ReadElementData(parentEnt);
                    if (currentData == null && XDataUtils.ReadStoryData(parentEnt) != null) break;
                }
                catch { break; }

                safetyCounter++;
            }

            return false;
        }

        /// <summary>
        /// Rule 3: Kiem tra hop le cho Reference (Cha phu).
        /// Su dung handle string thay vi truy cap truc tiep property Handle tren ElementData.
        /// </summary>
        public static bool CanAddReference(ElementData host, string hostHandle, string targetHandle)
        {
            if (host == null) return false;
            if (string.IsNullOrEmpty(hostHandle) || string.IsNullOrEmpty(targetHandle)) return false;
            if (hostHandle == targetHandle) return false; // Khong tu tham chieu
            if (host.OriginHandle == targetHandle) return false; // Khong trung Cha chinh
            if (host.ChildHandles != null && host.ChildHandles.Contains(targetHandle)) return false; // Khong tham chieu con minh
            return true;
        }
    }
}
