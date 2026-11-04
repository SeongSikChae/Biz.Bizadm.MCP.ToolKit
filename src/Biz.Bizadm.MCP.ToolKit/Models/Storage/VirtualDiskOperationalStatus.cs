using System.Text.Json.Serialization;

namespace Biz.Bizadm.MCP.ToolKit.Models.Storage;

/// <summary>가상 디스크 운영 상태(MSFT_VirtualDisk.OperationalStatus)</summary>
[JsonConverter(typeof(JsonStringEnumConverter<VirtualDiskOperationalStatus>))]
public enum VirtualDiskOperationalStatus : ushort
{
    /// <summary>알 수 없음</summary>
    Unknown = 0,

    /// <summary>기타</summary>
    Other = 1,

    /// <summary>정상</summary>
    OK = 2,

    /// <summary>성능 저하</summary>
    Degraded = 3,

    /// <summary>과부하</summary>
    Stressed = 4,

    /// <summary>오류 예측</summary>
    PredictiveFailure = 5,

    /// <summary>오류</summary>
    Error = 6,

    /// <summary>복구 불가 오류</summary>
    NonRecoverableError = 7,

    /// <summary>시작 중</summary>
    Starting = 8,

    /// <summary>중지 중</summary>
    Stopping = 9,

    /// <summary>중지됨</summary>
    Stopped = 10,

    /// <summary>서비스 중</summary>
    InService = 11,

    /// <summary>연결 없음</summary>
    NoContact = 12,

    /// <summary>통신 끊김</summary>
    LostCommunication = 13,

    /// <summary>중단됨</summary>
    Aborted = 14,

    /// <summary>휴면</summary>
    Dormant = 15,

    /// <summary>지원 개체 오류</summary>
    SupportingEntityInError = 16,

    /// <summary>완료</summary>
    Completed = 17,

    /// <summary>전원 모드</summary>
    PowerMode = 18,

    /// <summary>재배치 중</summary>
    Relocating = 19,

    /// <summary>분리됨(디스크 장치 개체 없음)</summary>
    Detached = 0xD002,

    /// <summary>불완전(복구에 필요한 중복성 부족)</summary>
    Incomplete = 0xD003,

    /// <summary>최적 아님</summary>
    Suboptimal = 0xD01B,

    /// <summary>중복성 없음</summary>
    NoRedundancy = 0xD024,
}
