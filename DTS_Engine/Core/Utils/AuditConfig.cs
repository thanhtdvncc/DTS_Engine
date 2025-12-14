namespace DTS_Engine.Core.Utils
{
    /// <summary>
    /// Centralized Configuration Constants for Audit Engine
    /// All "magic numbers" are consolidated here for easy tuning and maintenance.
    /// </summary>
    public static class AuditConfig
    {
        #region Story Grouping

        /// <summary>
        /// Tolerance (mm) for grouping elements into the same story level.
        /// Elements within this vertical distance are considered on the same floor.
        /// </summary>
        public const double STORY_TOLERANCE_MM = 200.0;

        /// <summary>
        /// Minimum story height (mm) to separate distinct floor levels.
        /// </summary>
        public const double MIN_STORY_HEIGHT_MM = 1500.0;

        #endregion

        #region Grid Snapping

        /// <summary>
        /// Tolerance (mm) for snapping elements to grid lines.
        /// Elements within this distance from a grid line are considered "on" that grid.
        /// </summary>
        public const double GRID_SNAP_TOLERANCE_MM = 250.0;

        #endregion

        #region Area Calculations

        /// <summary>
        /// Minimum area threshold (m²) for valid area elements.
        /// Areas smaller than this are considered degenerate/invalid.
        /// </summary>
        public const double MIN_AREA_THRESHOLD_M2 = 0.0001;

        #endregion

        #region Frame Classification (Vector-Based)

        /// <summary>
        /// Threshold for LocalAxis3.Z component to classify as Vertical element.
        /// If |L3.Z| < this value, element is considered Vertical (Column/Wall).
        /// If |L3.Z| > this value, element is considered Horizontal (Beam/Slab).
        /// Value 0.5 corresponds to ~60 degree angle from horizontal.
        /// </summary>
        public const double VERTICAL_AXIS_THRESHOLD = 0.5;

        /// <summary>
        /// Strict threshold for axis alignment detection.
        /// Direction Cosine must exceed this to be considered "aligned" with a global axis.
        /// </summary>
        public const double STRICT_AXIS_THRESHOLD = 0.9;

        /// <summary>
        /// Minimum length (mm) for 2D projection to be considered non-point-like.
        /// Elements with Length2D < this are considered vertical (columns).
        /// </summary>
        public const double MIN_LENGTH_2D_MM = 1.0;

        #endregion

        #region Load Processing

        /// <summary>
        /// Tolerance for considering load coverage as "full" (relative distance 0-1).
        /// </summary>
        public const double FULL_COVERAGE_TOLERANCE = 0.001;

        /// <summary>
        /// Minimum covered length (m) for valid frame loads.
        /// </summary>
        public const double MIN_COVERED_LENGTH_M = 1e-6;

        #endregion

        #region Report Formatting

        /// <summary>
        /// Maximum width for text report lines (characters).
        /// </summary>
        public const int MAX_REPORT_LINE_WIDTH = 140;

        /// <summary>
        /// Maximum elements to display in compressed element list.
        /// </summary>
        public const int MAX_ELEMENT_LIST_DISPLAY = 80;

        #endregion
    }
}
