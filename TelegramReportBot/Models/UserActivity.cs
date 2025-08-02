using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models
{
    /// <summary>
    /// Активность пользователей
    /// </summary>
    public class UserActivity
    {
        public int TotalUsers { get; set; }
        public int ActiveUsersToday { get; set; }
        public int ActiveUsersThisWeek { get; set; }

        public Dictionary<string, int> CommandUsage { get; set; } = new();
        public Dictionary<string, int> UserActions { get; set; } = new();

        public DateTime? LastUserActivity { get; set; }
        public string? MostActiveUser { get; set; }
    }

}
