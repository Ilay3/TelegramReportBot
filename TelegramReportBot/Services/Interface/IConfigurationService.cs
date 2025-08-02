using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TelegramReportBot.Models;

namespace TelegramReportBot.Services.Interface
{
    /// <summary>
    /// Интерфейс сервиса конфигурации
    /// </summary>
    public interface IConfigurationService
    {
        /// <summary>
        /// Получение текущей конфигурации
        /// </summary>
        Task<BotConfiguration> GetConfigurationAsync();

        /// <summary>
        /// Обновление конфигурации
        /// </summary>
        Task UpdateConfigurationAsync(BotConfiguration configuration);

        /// <summary>
        /// Перезагрузка конфигурации
        /// </summary>
        Task ReloadConfigurationAsync();

        /// <summary>
        /// Валидация конфигурации
        /// </summary>
        Task<ValidationResult> ValidateConfigurationAsync(BotConfiguration configuration);

        /// <summary>
        /// Экспорт конфигурации
        /// </summary>
        Task<string> ExportConfigurationAsync();

        /// <summary>
        /// Импорт конфигурации
        /// </summary>
        Task ImportConfigurationAsync(string configData);

        /// <summary>
        /// Получение значения конфигурации по ключу
        /// </summary>
        T GetConfigValue<T>(string key, T defaultValue = default);

        /// <summary>
        /// Установка значения конфигурации
        /// </summary>
        Task SetConfigValueAsync<T>(string key, T value);
    }
}
