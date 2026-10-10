using System.Text.Json.Serialization;

namespace GIMI_ModManager.Core.Services.GameBanana.ApiModels;

public class ApiModFileInfo
{
    [JsonPropertyName("_idRow")] public int FileId { get; init; } = -1;
    [JsonPropertyName("_sFile")] public string FileName { get; init; } = null!;
    [JsonPropertyName("_sDownloadUrl")] public string DownloadUrl { get; init; } = null!;

    [JsonPropertyName("_tsDateAdded")] public int DateAdded { get; init; } = -1;

    [JsonPropertyName("_sDescription")] public string Description { get; init; } = null!;

    [JsonPropertyName("_nFilesize")] public int FileSize { get; init; } = -1;

    [JsonPropertyName("_sAnalysisResultCode")]
    public string AnalysisResultCode { get; init; } = null!;

    /// <summary>GameBanana's antivirus verdict for this file (e.g. <c>clean</c>).</summary>
    [JsonPropertyName("_sAvResult")] public string AvResult { get; init; } = null!;

    /// <summary>GameBanana's analysis verdict (e.g. <c>ok</c>).</summary>
    [JsonPropertyName("_sAnalysisResult")] public string AnalysisResult { get; init; } = null!;

    /// <summary>Human-readable analysis verdict, e.g. "File passed preliminary analysis".</summary>
    [JsonPropertyName("_sAnalysisResultVerbose")]
    public string AnalysisResultVerbose { get; init; } = null!;

    [JsonPropertyName("_sMd5Checksum")] public string Md5Checksum { get; init; } = null!;

    [JsonPropertyName("_nDownloadCount")] public int DownloadCount { get; init; } = -1;
}