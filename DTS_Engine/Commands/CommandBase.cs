using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using DTS_Engine.Core.Utils;

namespace DTS_Engine.Commands
{
    /// <summary>
    /// Base class cho tất cả commands.
    /// Cung cấp các phương thức tiện ích chung cho việc tương tác với AutoCAD.
    /// </summary>
    public abstract class CommandBase
    {
        protected Document Doc => AcadUtils.Doc;
        protected Database Db => AcadUtils.Db;
        protected Editor Ed => AcadUtils.Ed;

        /// <summary>
        /// Ghi thông báo ra command line.
        /// </summary>
        protected void WriteMessage(string message)
        {
            Ed.WriteMessage($"\n{message}");
        }

        /// <summary>
        /// Ghi thông báo lỗi ra command line với định dạng chuyên nghiệp.
        /// </summary>
        protected void WriteError(string message)
        {
            Ed.WriteMessage($"\n>> LỖI: {message}");
        }

        /// <summary>
        /// Ghi thông báo thành công ra command line với định dạng chuyên nghiệp.
        /// </summary>
        protected void WriteSuccess(string message)
        {
            Ed.WriteMessage($"\n>> HOÀN TẤT: {message}");
        }

        /// <summary>
        /// Ghi thông báo cảnh báo ra command line.
        /// </summary>
        protected void WriteWarning(string message)
        {
            Ed.WriteMessage($"\n>> CẢNH BÁO: {message}");
        }

        /// <summary>
        /// Ghi thông báo thông tin ra command line.
        /// </summary>
        protected void WriteInfo(string message)
        {
            Ed.WriteMessage($"\n>> THÔNG TIN: {message}");
        }

        /// <summary>
        /// Thực hiện action trong transaction an toàn.
        /// </summary>
        protected void UsingTransaction(System.Action<Transaction> action)
        {
            AcadUtils.UsingTransaction(action);
        }

        /// <summary>
        /// Thực hiện function trong transaction an toàn và trả về kết quả.
        /// </summary>
        protected T UsingTransaction<T>(System.Func<Transaction, T> func)
        {
            return AcadUtils.UsingTransaction(func);
        }
    }
}