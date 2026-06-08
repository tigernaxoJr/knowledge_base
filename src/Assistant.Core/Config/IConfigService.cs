namespace Assistant.Core.Config;

/// <summary>設定讀寫服務介面</summary>
public interface IConfigService
{
    /// <summary>從持久化儲存載入設定</summary>
    Task<AppSettings> LoadAsync(CancellationToken ct = default);

    /// <summary>將設定持久化至儲存（ApiKey 加密後寫入）</summary>
    Task SaveAsync(AppSettings settings, CancellationToken ct = default);

    /// <summary>
    /// 對指定端點送出測試請求，驗證 ApiKey 與端點是否有效。
    /// </summary>
    /// <returns>成功時回傳 true；失敗時回傳錯誤訊息。</returns>
    Task<(bool Success, string? ErrorMessage)> TestConnectionAsync(
        string endpoint, string apiKey, string modelName, CancellationToken ct = default);
}
