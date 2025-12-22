# DTS System Refactor V5 - Star Topology Summary

## Mục tiêu ✅ HOÀN THÀNH
Loại bỏ phụ thuộc NOD (Named Object Dictionary), thay thế bằng Star Topology sử dụng hệ thống DTS_LINK có sẵn.

## Nguyên tắc cốt lõi

1. **Topology First**: Physical X-coordinate (L->R) là Truth duy nhất
2. **No NOD**: BeamGroup definitions là Runtime-only, không lưu trữ trong Dictionary
3. **Star Topology**: Phần tử bên trái nhất (S1) là "Mother", các phần tử khác link tới S1
4. **XData as Database**: Single Source of Truth là XData trên Line entity

## Files đã thay đổi

### Phase 1: Core Backend

#### 1. `TopologyBuilder.cs` ✅
- **Vị trí**: `DTS_Engine\Core\Algorithms\TopologyBuilder.cs`
- **Chức năng**:
  - `BuildGraph()`: Xây dựng topology graph từ selection
    - Step A: Expand selection để bao gồm Mother và Children
    - Step B: Sort theo X tăng dần (Left-to-Right)
    - Step C: Thiết lập Star Topology (optional)
  - `BuildBeamGroup()`: Chuyển topology thành runtime BeamGroup
  - `SplitIntoGroups()`: Chia danh sách topology thành các nhóm riêng biệt
  - **NEW V5.1**:
    - `GetDownstreamBeams()`: Tìm downstream beams từ một beam
    - `GetUpstreamBeams()`: Tìm upstream beams
    - `RelinkDownstreamToNewMother()`: Re-link downstream sau unlink
    - `ValidateAndRepairStarTopology()`: Validate và sửa chữa Star Topology

#### 2. `RebarCommands.cs` ✅
- **`DTS_REBAR_CALCULATE`**: Đã refactor để sử dụng TopologyBuilder
  - Không còn đọc/ghi NOD
  - Tự động xử lý geometry reversal (R->L beams)
  - Data persist trong XData của từng entity
- **`DTS_REBAR_VIEWER`** ✅ NEW: Mở viewer với runtime groups
  - Không load từ NOD
  - Scan all linked beams nếu selection rỗng
  - Refresh requirements từ XData
  - Load existing solution từ XData
- **Helpers mới**:
  - `FlipBeamResultData()`: Flip data cho R->L beams
  - `CheckIfGroupLocked()`: Check lock status từ XData
  - `ApplyGroupSolutionToEntitiesV5()`: Apply solution với reversal handling
  - `SyncGroupSpansToXData()`: (public static) Sync từ Viewer về XData
  - `RefreshGroupFromXDataV5()`: Refresh requirements từ XData
  - `LoadExistingSolutionFromXData()`: Load existing rebar solution
  - `ParseRebarString()`: Parse rebar string thành RebarInfo
  - `ApplyBeamGroupResultsV5()`: Apply results từ Viewer

### Phase 2: Commands

#### 3. `LinkCommands.cs` ✅
- **`DTS_REBAR_LINK`**: Tạo Star Topology cho nhóm dầm
- **`DTS_REBAR_UNLINK`**: Tách dầm khỏi nhóm với option follow downstream
- **`DTS_SHOW_REBAR_LINK`**: Hiển thị Star Topology của nhóm dầm
- **`DTS_CLEANUP_LEGACY`** ✅ NEW: Dọn dẹp NOD data legacy, repair Star Topology
- **`DTS_VALIDATE_TOPOLOGY`** ✅ NEW: Kiểm tra và báo cáo Star Topology integrity

### Phase 3: UI

#### 4. `BeamGroupViewerDialog.cs` ✅
- Constructor nhận runtime groups (không load từ NOD)
- `isV5Topology` flag để indicate data đã normalized
- `MarkGroupAsLockedInXData()`: Lock/unlock via XData
- `MarkGroupAsUnlockedInXData()`: Unlock via XData
- `ExtractSpanResultsForGroup()`: Đọc data từ XData
- `SendGroupUpdatedToWebViewAsync()`: Push updates về WebView
- `SendToastSimpleAsync()`: Gửi toast messages

#### 5. `BeamState.js`
- `isV5Topology` flag
- `validateGroupData()`: Validate data integrity
- `getCurrentGroupHandles()`: Get handles cho CAD highlighting
- `isCurrentGroupLocked()`: Check lock status

#### 6. `BeamActions.js`
- `lockDesign()`: Captures span edits, notifies C# to update XData
- `unlockDesign()`: Updates XData
- `highlightCurrentGroupInCAD()`: Highlight beams trong CAD
- `requestRecalculation()`: Request quick calc

#### 7. `BeamTable.js`
- `_formatAsReq()`: Show As_req values trong tooltip
- `highlightInCAD()`: Highlight span's beam trong CAD
- Per-row highlight button

### Phase 4: Utilities

#### 8. `UtilityCommands.cs` ✅
- `DTS_HELP` updated với V5 commands
- `DTS_VERSION` updated lên v5.0.0

#### 9. `XDataUtils.cs` ✅
- **`SaveBeamGroupsToNOD()`**: Marked `[Obsolete]`
- **`LoadBeamGroupsFromNOD()`**: Marked `[Obsolete]`
- **`ClearBeamGroupsFromNOD()`** ✅ NEW: Xóa legacy NOD data

## Danh sách Commands V5

| Command | Chức năng | Status |
|---------|-----------|--------|
| `DTS_REBAR_IMPORT_SAP` | Import SAP results | ✅ |
| `DTS_REBAR_CALCULATE` | Tính thép V5 Topology | ✅ |
| `DTS_REBAR_VIEWER` | Mở Beam Group Viewer | ✅ NEW |
| `DTS_REBAR_CALCULATE_SETTING` | Thiết lập tham số | ✅ |
| `DTS_REBAR_LINK` | Tạo Star Topology | ✅ |
| `DTS_REBAR_UNLINK` | Tách dầm khỏi nhóm | ✅ |
| `DTS_SHOW_REBAR_LINK` | Hiển thị Star Topology | ✅ |
| `DTS_CLEANUP_LEGACY` | Dọn dẹp NOD, repair links | ✅ NEW |
| `DTS_VALIDATE_TOPOLOGY` | Kiểm tra Star integrity | ✅ NEW |

## Workflow mới

### Tính thép:
```
1. User chọn dầm → DTS_REBAR_CALCULATE
2. TopologyBuilder.BuildGraph() → L->R sorted, Star Topology
3. V4RebarCalculator tính toán
4. ApplyGroupSolutionToEntitiesV5() → Ghi XData (với flip nếu cần)
5. (Optional) DTS_REBAR_VIEWER → Hiển thị và chỉnh sửa
6. Save → SyncGroupSpansToXData() → Ghi XData
```

### Liên kết nhóm:
```
1. DTS_REBAR_LINK → Chọn nhiều dầm
2. TopologyBuilder.BuildGraph() → S1 = Mother
3. XDataUtils.RegisterLink() → Thiết lập 2-way links
```

### Tách nhóm:
```
1. DTS_REBAR_UNLINK → Chọn dầm cần tách
2. Option: Yes = downstream follow, No = just unlink
3. XDataUtils.ClearAllLinks() / UnregisterLink()
4. (Optional) Re-link downstream to new Mother
```

### Migration V4 → V5:
```
1. DTS_VALIDATE_TOPOLOGY → Kiểm tra trạng thái
2. DTS_CLEANUP_LEGACY → Xóa NOD, repair Star Topology
3. DTS_REBAR_CALCULATE → Tính lại với V5 engine
```

## Data Flow

```
┌─────────────────────────────────────────────────────────────┐
│                        CAD Entity                           │
│  ┌─────────────────────────────────────────────────────┐   │
│  │                      XData                           │   │
│  │  - TopArea[], BotArea[] (SAP requirements)          │   │
│  │  - TopRebarString[], BotRebarString[] (solution)    │   │
│  │  - OriginHandle (link to Mother)                    │   │
│  │  - ChildHandles[] (links to Children)               │   │
│  │  - DesignLocked, LockedAt                           │   │
│  │  - IsManualModified, LastManualEdit                 │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                              ▲
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    TopologyBuilder                          │
│  - BuildGraph() → L->R sorted BeamTopology[]               │
│  - BuildBeamGroup() → Runtime BeamGroup                    │
│  - SplitIntoGroups() → Separate group detection            │
│  - GetDownstreamBeams() → Downstream tracking              │
│  - ValidateAndRepairStarTopology() → Self-healing          │
└─────────────────────────────────────────────────────────────┘
                              ▲
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                   V4RebarCalculator                         │
│  - CalculateProposals() → ContinuousBeamSolution[]         │
│  - ApplySolutionToGroup()                                   │
└─────────────────────────────────────────────────────────────┘
                              ▲
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                  BeamGroupViewerDialog                      │
│  - Receives runtime groups                                  │
│  - WebView2 UI                                              │
│  - SyncGroupSpansToXData() on Save/Apply                    │
└─────────────────────────────────────────────────────────────┘
```

## Backward Compatibility

- `LoadBeamGroupsFromNOD()` và `SaveBeamGroupsToNOD()` được giữ lại với `[Obsolete]` attribute
- Khi gọi sẽ log warning và vẫn hoạt động (không throw)
- `ClearBeamGroupsFromNOD()` được thêm để xóa legacy data
- XData keys tương thích với format cũ

## Test Cases

### Test 1: The "Reversed Beam"
- Draw Beam A (L->R) và Beam B (R->L)
- Select both → Calc
- Expectation: Viewer shows S1, S2 correctly. Internal inputs cho B được flip.

### Test 2: The "Split"
- Draw A-B-C. Link A(Mother)-B-C.
- Unlink B.
- Run Calc on A & C.
- Expectation: 2 Separate groups.

### Test 3: The "Merge"
- Select Group A & Group C.
- Run DTS_REBAR_LINK.
- Run Calc.
- Expectation: 1 group (A-C). Indices re-assigned.

### Test 4: The "Legacy Migration"
- Open old drawing with NOD data.
- Run DTS_VALIDATE_TOPOLOGY → Should report NOD exists.
- Run DTS_CLEANUP_LEGACY → Should clear NOD, repair topology.
- Run DTS_VALIDATE_TOPOLOGY → Should report all valid.

### Test 5: The "Downstream Follow"
- Link A-B-C-D (A = Mother).
- Select B → DTS_REBAR_UNLINK → Yes (follow downstream).
- Expectation: B becomes new Mother for C, D. A is alone.
