using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models
{
    /// <summary>
    /// Статистика ошибок
    /// </summary>
    public class ErrorStatistics
    {
        public int TotalErrors { get; set; }
        public int ErrorsToday { get; set; }
        public int ErrorsThisWeek { get; set; }

        public int NetworkErrors { get; set; }
        public int FileSystemErrors { get; set; }
        public int TelegramApiErrors { get; set; }
        public int ValidationErrors { get; set; }

        public DateTime? LastError { get; set; }
        public string? LastErrorMessage { get; set; }
    }

}
