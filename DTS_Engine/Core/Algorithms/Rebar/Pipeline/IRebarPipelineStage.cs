using System.Collections.Generic;

namespace DTS_Engine.Core.Algorithms.Rebar.Pipeline
{
    /// <summary>
    /// Interface cho mỗi stage trong pipeline tính thép.
    /// Stage có thể xử lý 1 context hoặc sinh ra nhiều contexts.
    /// </summary>
    public interface IRebarPipelineStage
    {
        /// <summary>
        /// Tên stage (dùng để debug và logging).
        /// </summary>
        string StageName { get; }

        /// <summary>
        /// Thứ tự thực thi (1 = đầu tiên).
        /// </summary>
        int Order { get; }

        /// <summary>
        /// Xử lý batch contexts. Có thể:
        /// - Lọc bớt (remove invalid)
        /// - Biến đổi (modify in-place)
        /// - Nhân bản (1 input → N outputs, như ScenarioGenerator)
        /// </summary>
        /// <param name="contexts">Danh sách contexts từ stage trước</param>
        /// <param name="globalConstraints">Ràng buộc toàn dự án</param>
        /// <returns>Danh sách contexts đã xử lý</returns>
        IEnumerable<Models.SolutionContext> Execute(
            IEnumerable<Models.SolutionContext> contexts,
            Models.ProjectConstraints globalConstraints);
    }
}
