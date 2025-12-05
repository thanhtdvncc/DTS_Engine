using Autodesk.AutoCAD.DatabaseServices;
using DTS_Engine.Core.Data;

namespace DTS_Engine.Core.Utils
{
    public static class LinkRules
    {
        /// <summary>
        /// Rule 1: Phân c?p nghiêm ng?t cho Cha Chính (Primary Parent)
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
        /// Rule 2: Ch?ng vòng l?p (Acyclic Check).
        /// Duy?t ng??c t? Parent lên trên, n?u g?p Child Handle thì là vòng l?p.
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
        /// Rule 3: Ki?m tra h?p l? cho Reference (Cha ph?)
        /// S? d?ng handle string thay vì truy c?p tr?c ti?p property Handle trên ElementData.
        /// </summary>
        public static bool CanAddReference(ElementData host, string hostHandle, string targetHandle)
        {
            if (host == null) return false;
            if (string.IsNullOrEmpty(hostHandle) || string.IsNullOrEmpty(targetHandle)) return false;
            if (hostHandle == targetHandle) return false; // Không t? tham chi?u
            if (host.OriginHandle == targetHandle) return false; // Không trùng Cha chính
            if (host.ChildHandles != null && host.ChildHandles.Contains(targetHandle)) return false; // Không tham chi?u con mình
            return true;
        }
    }
}
