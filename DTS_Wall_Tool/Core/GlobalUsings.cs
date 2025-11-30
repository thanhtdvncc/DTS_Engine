// File này cung cấp type aliases để code cũ vẫn hoạt động
// Đặt trong Core/ hoặc gốc project

// Re-export types từ Primitives vào namespace Core
global using Point2D = DTS_Wall_Tool.Core.Primitives.Point2D;
global using LineSegment2D = DTS_Wall_Tool.Core.Primitives.LineSegment2D;
global using BoundingBox = DTS_Wall_Tool.Core.Primitives.BoundingBox;
global using ProjectionResult = DTS_Wall_Tool.Core.Primitives.ProjectionResult;
global using OverlapResult = DTS_Wall_Tool.Core.Primitives.OverlapResult;

// Re-export Data types
global using WallData = DTS_Wall_Tool.Core.Data.WallData;
global using StoryData = DTS_Wall_Tool.Core.Data.StoryData;
global using MappingRecord = DTS_Wall_Tool.Core.Data.MappingRecord;
global using SapFrame = DTS_Wall_Tool.Core.Data.SapFrame;