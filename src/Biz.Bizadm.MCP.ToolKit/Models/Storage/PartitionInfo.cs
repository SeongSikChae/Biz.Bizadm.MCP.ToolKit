using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>파티션 정보(MSFT_Partition)</summary>
/// <remarks>출력 스키마가 모든 속성을 required로 두므로 nullable 속성도 null을 그대로 쓴다.</remarks>
/// <param name="DiskNumber">디스크 번호</param>
/// <param name="PartitionNumber">파티션 번호</param>
/// <param name="DriveLetter">드라이브 문자, 없으면 null</param>
/// <param name="AccessPaths">접근 경로 목록</param>
/// <param name="Offset">디스크 내 시작 위치(byte)</param>
/// <param name="Size">크기(byte)</param>
/// <param name="GptType">GPT 파티션 유형 GUID, MBR 디스크면 null</param>
/// <param name="MbrType">MBR 파티션 유형 코드, GPT 디스크면 null</param>
/// <param name="IsActive">활성 파티션 여부(MBR)</param>
/// <param name="IsHidden">숨김 여부</param>
/// <param name="IsReadOnly">읽기 전용 여부</param>
/// <param name="IsOffline">오프라인 여부</param>
public sealed record PartitionInfo(
    uint DiskNumber,
    uint PartitionNumber,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? DriveLetter,
    IReadOnlyList<string> AccessPaths,
    ulong Offset,
    ulong Size,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? GptType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] ushort? MbrType,
    bool IsActive,
    bool IsHidden,
    bool IsReadOnly,
    bool IsOffline);
