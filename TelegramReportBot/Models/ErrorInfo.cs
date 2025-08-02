using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models
{
    /// <summary>
    /// Информация об ошибке
    /// </summary>
    public class ErrorInfo
    {
        public DateTime OccurredAt { get; set; }
        public string ErrorType { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? StackTrace { get; set; }
        public string? FileName { get; set; }
        public string? UserId { get; set; }
    }

}
