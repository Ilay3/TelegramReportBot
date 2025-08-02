using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Core.Models.Configuration
{
    /// <summary>
    /// Настройки уведомлений
    /// </summary>
    public class NotificationSettings
    {
        /// <summary>
        /// Отправлять уведомления о запуске
        /// </summary>
        public bool SendStartupNotifications { get; set; } = true;

        /// <summary>
        /// Отправлять уведомления о завершении работы
        /// </summary>
        public bool SendShutdownNotifications { get; set; } = true;

        /// <summary>
        /// Отправлять уведомления об ошибках
        /// </summary>
        public bool SendErrorNotifications { get; set; } = true;

        /// <summary>
        /// Отправлять ежедневные отчёты
        /// </summary>
        public bool SendDailyReports { get; set; } = true;

        /// <summary>
        /// Время отправки ежедневного отчёта
        /// </summary>
        public string DailyReportTime { get; set; } = "09:00";
    }

}
