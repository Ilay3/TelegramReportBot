using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Core.Models.Statistics
{
    /// <summary>
    /// Активность пользователей
    /// </summary>
    public class UserActivity
    {
        /// <summary>
        /// Всего пользователей
        /// </summary>
        public int TotalUsers { get; set; }

        /// <summary>
        /// Активных пользователей сегодня
        /// </summary>
        public int ActiveUsersToday { get; set; }

        /// <summary>
        /// Активных пользователей на этой неделе
        /// </summary>
        public int ActiveUsersThisWeek { get; set; }

        /// <summary>
        /// Использование команд
        /// </summary>
        public Dictionary<string, int> CommandUsage { get; set; } = new();

        /// <summary>
        /// Действия пользователей
        /// </summary>
        public Dictionary<string, int> UserActions { get; set; } = new();

        /// <summary>
        /// Время последней активности пользователя
        /// </summary>
        public DateTime? LastUserActivity { get; set; }

        /// <summary>
        /// Самый активный пользователь
        /// </summary>
        public string? MostActiveUser { get; set; }
    }

}
