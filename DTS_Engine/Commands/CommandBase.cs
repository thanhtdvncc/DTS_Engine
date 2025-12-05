using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using DTS_Engine.Core.Utils;

namespace DTS_Engine.Commands
{
    /// <summary>
    /// Base class cho tất cả commands
    /// </summary>
    public abstract class CommandBase
    {
        protected Document Doc => AcadUtils.Doc;
        protected Database Db => AcadUtils.Db;
        protected Editor Ed => AcadUtils.Ed;

        /// <summary>
        /// Ghi message ra command line
        /// </summary>
        protected void WriteMessage(string message)
        {
            Ed.WriteMessage("\n" + message);
        }

        /// <summary>
        /// Ghi lỗi ra command line
        /// </summary>
        protected void WriteError(string message)
        {
            Ed.WriteMessage("\n[LỖI] " + message);
        }

        /// <summary>
        /// Ghi thành công ra command line
        /// </summary>
        protected void WriteSuccess(string message)
        {
            Ed.WriteMessage("\n[OK] " + message);
        }

        /// <summary>
        /// Thực hiện action trong transaction
        /// </summary>
        protected void UsingTransaction(System.Action<Transaction> action)
        {
            AcadUtils.UsingTransaction(action);
        }

        /// <summary>
        /// Thực hiện function trong transaction
        /// </summary>
        protected T UsingTransaction<T>(System.Func<Transaction, T> func)
        {
            return AcadUtils.UsingTransaction(func);
        }
    }
}