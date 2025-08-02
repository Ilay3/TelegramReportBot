namespace TelegramReportBot.Core.Enums;

/// <summary>
/// Административные команды
/// </summary>
public enum AdminCommand
{
    /// <summary>
    /// Очистить список отправленных файлов
    /// </summary>
    ClearSentFiles,

    /// <summary>
    /// Принудительное создание резервной копии
    /// </summary>
    ForceBackup,

    /// <summary>
    /// Перезагрузить конфигурацию
    /// </summary>
    ReloadConfig,

    /// <summary>
    /// Перезапустить бота
    /// </summary>
    RestartBot,

    /// <summary>
    /// Просмотр логов
    /// </summary>
    ViewLogs,

    /// <summary>
    /// Экспорт статистики
    /// </summary>
    ExportStatistics,

    /// <summary>
    /// Тест соединения
    /// </summary>
    TestConnection,

    /// <summary>
    /// Очистка файлов
    /// </summary>
    CleanupFiles,

    /// <summary>
    /// Обновление настроек
    /// </summary>
    UpdateSettings
}

/// <summary>
/// Уровень безопасности события
/// </summary>
public enum SecurityLevel
{
    /// <summary>
    /// Информационный уровень
    /// </summary>
    Info,

    /// <summary>
    /// Предупреждение
    /// </summary>
    Warning,

    /// <summary>
    /// Критический уровень
    /// </summary>
    Critical
}