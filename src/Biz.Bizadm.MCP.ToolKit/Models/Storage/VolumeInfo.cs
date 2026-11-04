using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>볼륨 정보(MSFT_Volume)</summary>
/// <remarks>출력 스키마가 모든 속성을 required로 두므로 nullable 속성도 null을 그대로 쓴다.</remarks>
/// <param name="UniqueId">볼륨 고유 ID</param>
/// <param name="DriveLetter">드라이브 문자, 없으면 null</param>
/// <param name="FileSystem">파일 시스템, 포맷되지 않았으면 null</param>
/// <param name="FileSystemLabel">볼륨 레이블, 없으면 null</param>
/// <param name="AllocationUnitSize">할당 단위 크기(byte)</param>
/// <param name="Size">크기(byte)</param>
/// <param name="SizeRemaining">남은 용량(byte)</param>
/// <param name="HealthStatus">상태</param>
public sealed record VolumeInfo(
    string UniqueId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? DriveLetter,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FileSystem,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FileSystemLabel,
    uint AllocationUnitSize,
    ulong Size,
    ulong SizeRemaining,
    StorageHealthStatus HealthStatus);
