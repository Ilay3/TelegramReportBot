using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramReportBot.Models;

namespace TelegramReportBot.Services.Interface
{
    /// <summary>
    /// Интерфейс сервиса резервного копирования
    /// </summary>
    public interface IBackupService
    {
        /// <summary>
        /// Создание резервной копии
        /// </summary>
        Task<string> CreateBackupAsync();

        /// <summary>
        /// Восстановление из резервной копии
        /// </summary>
        Task RestoreBackupAsync(string backupPath);

        /// <summary>
        /// Получение списка доступных резервных копий
        /// </summary>
        Task<List<BackupInfo>> GetAvailableBackupsAsync();

        /// <summary>
        /// Удаление старых резервных копий
        /// </summary>
        Task CleanupOldBackupsAsync();

        /// <summary>
        /// Проверка целостности резервной копии
        /// </summary>
        Task<bool> VerifyBackupAsync(string backupPath);
    }
}
