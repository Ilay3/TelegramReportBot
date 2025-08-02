using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Core.Models.Configuration
{
    /// <summary>
    /// Настройки ограничения скорости
    /// </summary>
    public class RateLimitingSettings
    {
        /// <summary>
        /// Максимум файлов в минуту
        /// </summary>
        public int MaxFilesPerMinute { get; set; } = 10;

        /// <summary>
        /// Максимум файлов в час
        /// </summary>
        public int MaxFilesPerHour { get; set; } = 100;

        /// <summary>
        /// Задержка между загрузками (мс)
        /// </summary>
        public int CooldownBetweenUploads { get; set; } = 1000;

        /// <summary>
        /// Максимум попыток повтора
        /// </summary>
        public int MaxRetries { get; set; } = 3;
    }

}
