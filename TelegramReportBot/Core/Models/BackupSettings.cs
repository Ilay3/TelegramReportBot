namespace TelegramReportBot.Common.Models;

/// <summary>
/// Настройки резервного копирования
/// </summary>
public class BackupSettings
{
    /// <summary>
    /// Включить автоматическое резервное копирование
    /// </summary>
    public bool EnableAutoBackup { get; set; } = true;

    /// <summary>
    /// Интервал резервного копирования в часах
    /// </summary>
    public int BackupIntervalHours { get; set; } = 24;

    /// <summary>
    /// Количество дней хранения резервных копий
    /// </summary>
    public int BackupRetentionDays { get; set; } = 30;

    /// <summary>
    /// Путь для сохранения резервных копий
    /// </summary>
    public string BackupLocation { get; set; } = "Backups";

    /// <summary>
    /// Сжимать резервные копии
    /// </summary>
    public bool CompressBackups { get; set; } = true;

    /// <summary>
    /// Включать базу данных в резервную копию
    /// </summary>
    public bool BackupDatabase { get; set; } = true;

    /// <summary>
    /// Включать логи в резервную копию
    /// </summary>
    public bool BackupLogs { get; set; } = false;
}