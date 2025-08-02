using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Core.Models.Security
{
    /// <summary>
    /// Состояние здоровья системы
    /// </summary>
    public class HealthStatus
    {
        public bool IsHealthy { get; set; } = true;
        public List<string> Issues { get; set; } = new();
        public Dictionary<string, object> Metrics { get; set; } = new();
        public DateTime CheckedAt { get; set; } = DateTime.Now;
    }

}
