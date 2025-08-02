using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TelegramReportBot.Models.Enum
{
    /// <summary>
    /// Тип уведомления
    /// </summary>
    public enum NotificationType
    {
        Info,
        Warning,
        Error,
        Success,
        System
    }
}
