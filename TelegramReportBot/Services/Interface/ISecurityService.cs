using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramReportBot.Models;

namespace TelegramReportBot.Services.Interface
{
    /// <summary>
    /// Интерфейс сервиса безопасности
    /// </summary>
    public interface ISecurityService
    {
        /// <summary>
        /// Проверка прав администратора
        /// </summary>
        Task<bool> IsAdminUserAsync(string userId);

        /// <summary>
        /// Проверка безопасности файла
        /// </summary>
        Task<bool> IsFileSafeAsync(string filePath);

        /// <summary>
        /// Валидация файла
        /// </summary>
        Task<ValidationResult> ValidateFileAsync(string filePath);

        /// <summary>
        /// Выполнение проверки безопасности системы
        /// </summary>
        Task PerformSecurityCheckAsync();

        /// <summary>
        /// Логирование события безопасности
        /// </summary>
        void LogSecurityEvent(SecurityEvent securityEvent);

        /// <summary>
        /// Получение аудит-лога
        /// </summary>
        Task<List<AuditEvent>> GetAuditLogAsync(DateTime? from = null, DateTime? to = null);

        /// <summary>
        /// Блокировка подозрительного пользователя
        /// </summary>
        Task BlockUserAsync(string userId, string reason);

        /// <summary>
        /// Разблокировка пользователя
        /// </summary>
        Task UnblockUserAsync(string userId);
    }
}
